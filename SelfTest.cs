using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using XdsConverter;

static class SelfTest
{
    static int fails = 0;
    static void Check(bool ok, string msg) { if (!ok) fails++; if (!ok || msg.StartsWith("#")) Console.WriteLine((ok ? "OK   " : "FAIL ") + msg); }

    static double MeanAbsDiff(Bitmap a, Bitmap b) { return MeanAbsDiff(a, b, 15); }
    static double MeanAbsDiff(Bitmap a, Bitmap b, int mask)
    {
        double sum = 0; long n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += ((mask & 1) != 0 ? Math.Abs(p.R - q.R) : 0) + ((mask & 2) != 0 ? Math.Abs(p.G - q.G) : 0) + ((mask & 4) != 0 ? Math.Abs(p.B - q.B) : 0) + ((mask & 8) != 0 ? Math.Abs(p.A - q.A) : 0);
                n += ((mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1) + ((mask >> 3) & 1));
            }
        return sum / n;
    }

    static int Main(string[] args)
    {
        string texconv = args[0], work = args[1];
        Directory.CreateDirectory(work);
        List<string> files = new List<string>();
        for (int i = 2; i < args.Length; i++)
            files.AddRange(Directory.GetFiles(args[i], "*.xds", SearchOption.AllDirectories));
        Console.WriteLine("files: " + files.Count);

        // 1) header + data size regeneration for every real file
        int ok = 0, bad = 0, unsupported = 0;
        foreach (string f in files)
        {
            XdsTexture t;
            try { t = XdsTexture.FromFile(f); } catch (Exception ex) { unsupported++; Console.WriteLine("  skip " + Path.GetFileName(f) + ": " + ex.Message); continue; }
            bool gamma = t.IsGamma || (t.Codec == DxtFormat.A8R8G8B8 && t.Format == 50);
            bool mips = t.MipMaxLevel > 0;
            XdsTexture n = XdsTexture.Create(t.Width, t.Height, t.Codec, gamma, mips);
            bool same = n.Data.Length == t.Data.Length;
            for (int i = 0; i < 13 && same; i++) if (n.Header[i] != t.Header[i]) same = false;
            if (!same)
            {
                bad++;
                if (bad <= 10)
                {
                    Console.WriteLine("  MISMATCH " + f.Substring(Math.Max(0, f.Length - 60)) + " " + t);
                    for (int i = 0; i < 13; i++) if (n.Header[i] != t.Header[i]) Console.WriteLine("     H" + i + " real=" + t.Header[i].ToString("X") + " gen=" + n.Header[i].ToString("X"));
                    if (n.Data.Length != t.Data.Length) Console.WriteLine("     data real=" + t.Data.Length + " gen=" + n.Data.Length);
                }
            }
            else ok++;
        }
        Check(bad == 0, "#header/data regeneration: " + ok + " identical, " + bad + " different, " + unsupported + " unsupported");

        // 2) decode all levels of all files without errors
        int decErr = 0;
        foreach (string f in files)
        {
            try
            {
                XdsTexture t = XdsTexture.FromFile(f);
                for (int l = 0; l < t.LevelCount; l++) using (t.GetLevelBitmap(l)) { }
            }
            catch (NotSupportedException) { }
            catch (Exception ex) { decErr++; Console.WriteLine("  decode error " + f + ": " + ex.Message); }
        }
        Check(decErr == 0, "#decode all levels");

        // 3) real-world comparison: xds vs the bmp/tga exported by the artist
        foreach (string pair in new string[] { "nodamage_lod0" })
        {
            foreach (string dir in args)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string xf in Directory.GetFiles(dir, pair + ".xds", SearchOption.AllDirectories))
                {
                    string bmpPath = Path.ChangeExtension(xf, ".bmp");
                    if (!File.Exists(bmpPath)) continue;
                    using (Bitmap a = XdsTexture.FromFile(xf).GetLevelBitmap(0))
                    using (Bitmap b = XdsTexture.LoadBitmap(bmpPath))
                    {
                        if (a.Width != b.Width || a.Height != b.Height) { Console.WriteLine("  size differs " + a.Size + " vs " + b.Size); continue; }
                        double d = MeanAbsDiff(a, b);
                        Console.WriteLine("  compare " + Path.GetFileName(xf) + " vs bmp: mean abs diff/channel = " + d.ToString("0.00"));
                    }
                }
            }
        }

        // 4) import round trip through texconv: export -> import -> decode, per format
        string[] samples = new string[] { "carbonFiber.xds", "carbonFiberNormal.xds", "bumper_frame.xds", "grille1.xds", "CircleQuad_11.xds", "Chart_Curve.xds" };
        foreach (string name in samples)
        {
            string f = files.Find(delegate(string s) { return Path.GetFileName(s) == name; });
            if (f == null) { Console.WriteLine("  (sample missing: " + name + ")"); continue; }
            XdsTexture t = XdsTexture.FromFile(f);
            byte[] origBytes = File.ReadAllBytes(f);
            string png = Path.Combine(work, name + ".png");
            using (Bitmap b = t.GetLevelBitmap(0)) b.Save(png, ImageFormat.Png);
            XdsTexture copy = new XdsTexture(origBytes);
            copy.ImportFile(png, texconv, true);
            byte[] outBytes = copy.ToBytes();
            Check(outBytes.Length == origBytes.Length, "#" + name + " " + t + " -> re-import keeps size (" + outBytes.Length + ")");
            bool hdr = true; for (int i = 0; i < 52; i++) if (outBytes[i] != origBytes[i]) hdr = false;
            Check(hdr, "#" + name + " header unchanged");
            using (Bitmap a = t.GetLevelBitmap(0))
            using (Bitmap b = copy.GetLevelBitmap(0))
                Console.WriteLine("    " + name + " level0 diff after re-compress: " + MeanAbsDiff(a, b).ToString("0.00"));
            if (t.LevelCount > 1)
                using (Bitmap a = t.GetLevelBitmap(t.LevelCount - 1))
                using (Bitmap b = copy.GetLevelBitmap(t.LevelCount - 1))
                    Console.WriteLine("    " + name + " last mip diff: " + MeanAbsDiff(a, b).ToString("0.00"));
        }

        // 5) brand new textures: every format, a gradient image, save + reload
        DxtFormat[] fmts = new DxtFormat[] { DxtFormat.Dxt1, DxtFormat.Dxt3, DxtFormat.Dxt5, DxtFormat.Dxn, DxtFormat.A8R8G8B8 };
        int[][] sizes = new int[][] { new int[] { 256, 256 }, new int[] { 300, 100 }, new int[] { 32, 16 }, new int[] { 1024, 512 } };
        foreach (DxtFormat fm in fmts)
            foreach (int[] sz in sizes)
                foreach (bool gm in new bool[] { true, false })
                {
                    XdsTexture t = XdsTexture.Create(sz[0], sz[1], fm, gm, true);
                    string png = Path.Combine(work, "grad.png");
                    using (Bitmap g = new Bitmap(sz[0], sz[1], PixelFormat.Format32bppArgb))
                    {
                        for (int y = 0; y < sz[1]; y++) for (int x = 0; x < sz[0]; x++)
                            g.SetPixel(x, y, Color.FromArgb((fm == DxtFormat.Dxt1 || fm == DxtFormat.Dxn) ? 255 : 255 - x * 255 / sz[0], 255 - x * 255 / sz[0], x * 255 / sz[0], y * 255 / sz[1]));
                        g.Save(png, ImageFormat.Png);
                    }
                    t.ImportFile(png, texconv, true);
                    XdsTexture r = new XdsTexture(t.ToBytes());
                    bool good = r.Width == sz[0] && r.Height == sz[1] && r.Codec == fm && r.LevelCount == XdsTexture.Log2Floor(Math.Max(sz[0], sz[1])) + 1;
                    using (Bitmap a = new Bitmap(png)) using (Bitmap b = r.GetLevelBitmap(0))
                    {
                        double d = MeanAbsDiff(a, b, fm == DxtFormat.Dxn ? 3 : 15);
                        bool lossOk = d < (fm == DxtFormat.Dxn ? 40 : 6);
                        Check(good && lossOk, (good && lossOk ? "#" : "") + "new " + XdsTexture.FormatName(fm) + " " + sz[0] + "x" + sz[1] + (gm ? " sRGB" : " linear") + " " + r + " diff=" + d.ToString("0.00"));
                    }
                }
        Console.WriteLine(fails == 0 ? "ALL OK" : ("FAILED: " + fails));
        return fails;
    }
}


