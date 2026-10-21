namespace XdsConverter
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.IO;

    public partial class XdsTexture
    {
        // Builds an empty texture (transparent black) with the header the game files use for that
        // format. The rules (pitch, mip page, packed tail...) were checked against 274 real .xds files.
        public static XdsTexture Create(int width, int height, DxtFormat codec, bool gamma, bool mips)
        {
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
            {
                throw new ArgumentException("Size must be between 1 and 8192.");
            }
            bool is8888 = codec == DxtFormat.A8R8G8B8;
            if (codec == DxtFormat.Dxn)
            {
                gamma = false;
            }
            int format;
            switch (codec)
            {
                case DxtFormat.Dxt1: format = gamma ? 51 : 18; break;
                case DxtFormat.Dxt3: format = gamma ? 52 : 19; break;
                case DxtFormat.Dxt5: format = gamma ? 53 : 20; break;
                case DxtFormat.Dxn: format = 49; break;
                case DxtFormat.A8R8G8B8: format = gamma ? 50 : 6; break;
                default: throw new NotSupportedException("Unsupported format.");
            }
            // gamma 8888 textures are stored linear (untiled) in the game files
            bool tiled = !(is8888 && gamma);
            int endian = is8888 ? 2 : 1;
            int blockSize = is8888 ? 1 : 4;
            // textures whose smaller side is <= 16 keep the whole mip tail in the base level and
            // use a power of two pitch (e.g. 360x16 -> 512)
            bool smallTail = Log2Ceil(Math.Min(width, height)) <= 4;
            int pitchTexels = Align(DivUp(smallTail ? NextPow2(width) : width, blockSize), TileBlocks) * blockSize;

            XdsTexture t = new XdsTexture();
            t.Header[0] = 3;
            t.Header[1] = 1;
            t.Header[5] = 0xFFFF0000;
            t.Header[6] = 0xFFFF0000;
            t.Header[7] = (tiled ? 0x80000000u : 0u) | ((uint) (pitchTexels / 32) << 22) | ((gamma ? 0x3Fu : 0u) << 2) | 2u;
            t.Header[8] = (uint) (format | (endian << 6));
            t.Header[9] = (uint) ((width - 1) | ((height - 1) << 13));
            t.Header[10] = is8888 ? 0xC14u : 0xD10u;
            int maxLevel = mips ? Log2Floor(Math.Max(width, height)) : 0;
            t.Header[11] = (uint) (maxLevel << 6);
            t.Header[12] = 0x200 | 0x800;   // 2D, packed mips

            int baseBytes = t.StorageStride(0, true);
            int dataLength = baseBytes;
            int packedLevel = t.PackedLevel;
            if (mips && packedLevel > 0)
            {
                t.Header[12] |= (uint) (baseBytes / PageSize) << 12;
                dataLength = baseBytes;
                for (int l = 1; l <= packedLevel; l++)
                {
                    dataLength += t.StorageStride(l, false);
                }
            }
            t.Data = new byte[dataLength];
            return t;
        }

        public static string FormatName(DxtFormat codec)
        {
            switch (codec)
            {
                case DxtFormat.Dxt1: return "DXT1";
                case DxtFormat.Dxt3: return "DXT3";
                case DxtFormat.Dxt5: return "DXT5";
                case DxtFormat.Dxn: return "DXN (BC5)";
                case DxtFormat.Dxt5A: return "DXT5A (BC4)";
                case DxtFormat.A8R8G8B8: return "A8R8G8B8";
            }
            return "Unknown";
        }

        // Imports an image of any size: if it differs from the texture it is scaled first.
        public void ImportFile(string imagePath, string texconvPath, bool allowResize)
        {
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            if (ext == ".dds" || !allowResize)
            {
                this.ImportImage(imagePath, texconvPath);
                return;
            }
            Size size = ImageSize(imagePath);
            if (size.Width == this.Width && size.Height == this.Height)
            {
                this.ImportImage(imagePath, texconvPath);
                return;
            }
            string tmp = Path.Combine(Path.GetTempPath(), "xdsconv_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (Bitmap src = LoadBitmap(imagePath))
                using (Bitmap dst = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(dst))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(src, new Rectangle(0, 0, this.Width, this.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    dst.Save(tmp, ImageFormat.Png);
                }
                this.ImportImage(tmp, texconvPath);
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }

        public static Size ImageSize(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tga")
            {
                byte[] h = new byte[18];
                using (FileStream s = File.OpenRead(path)) { s.Read(h, 0, h.Length); }
                return new Size(BitConverter.ToUInt16(h, 12), BitConverter.ToUInt16(h, 14));
            }
            if (ext == ".dds")
            {
                byte[] h = new byte[24];
                using (FileStream s = File.OpenRead(path)) { s.Read(h, 0, h.Length); }
                return new Size(BitConverter.ToInt32(h, 16), BitConverter.ToInt32(h, 12));
            }
            using (Image i = Image.FromFile(path)) { return new Size(i.Width, i.Height); }
        }

        // Loads PNG/BMP/JPG/GIF/TIFF/TGA as 32bpp ARGB.
        public static Bitmap LoadBitmap(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() == ".tga")
            {
                return ReadTga(path);
            }
            using (Image img = Image.FromFile(path))
            {
                Bitmap bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                }
                return bmp;
            }
        }

        public static void SaveBitmap(Bitmap bmp, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tga") { WriteTga(bmp, path); }
            else if (ext == ".bmp") { bmp.Save(path, ImageFormat.Bmp); }
            else { bmp.Save(path, ImageFormat.Png); }
        }

        // Uncompressed 32 bit TGA, top-left origin.
        public static void WriteTga(Bitmap bmp, string path)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    byte[] hdr = new byte[18];
                    hdr[2] = 2;
                    hdr[12] = (byte) w; hdr[13] = (byte) (w >> 8);
                    hdr[14] = (byte) h; hdr[15] = (byte) (h >> 8);
                    hdr[16] = 32;
                    hdr[17] = 0x28;
                    fs.Write(hdr, 0, hdr.Length);
                    byte[] row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long) y * d.Stride), row, 0, row.Length);
                        fs.Write(row, 0, row.Length);
                    }
                }
            }
            finally { bmp.UnlockBits(d); }
        }

        public static Bitmap ReadTga(string path)
        {
            byte[] f = File.ReadAllBytes(path);
            int idLen = f[0], cmapType = f[1], type = f[2];
            int w = BitConverter.ToUInt16(f, 12), h = BitConverter.ToUInt16(f, 14), bpp = f[16], desc = f[17];
            if (cmapType != 0 || (type != 2 && type != 10 && type != 3 && type != 11) || (bpp != 32 && bpp != 24 && bpp != 8))
            {
                throw new NotSupportedException("Unsupported TGA type (use 8/24/32 bit, uncompressed or RLE).");
            }
            int bytes = bpp / 8;
            byte[] px = new byte[w * h * bytes];
            int p = 18 + idLen;
            if (type == 2 || type == 3)
            {
                Buffer.BlockCopy(f, p, px, 0, Math.Min(px.Length, f.Length - p));
            }
            else
            {
                int o = 0;
                while (o < px.Length && p < f.Length)
                {
                    int c = f[p++];
                    int n = (c & 0x7F) + 1;
                    if ((c & 0x80) != 0)
                    {
                        for (int i = 0; i < n && o < px.Length; i++) { for (int k = 0; k < bytes; k++) { px[o++] = f[p + k]; } }
                        p += bytes;
                    }
                    else
                    {
                        for (int i = 0; i < n * bytes && o < px.Length; i++) { px[o++] = f[p++]; }
                    }
                }
            }
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                bool topDown = (desc & 0x20) != 0;
                for (int y = 0; y < h; y++)
                {
                    int sy = topDown ? y : h - 1 - y;
                    for (int x = 0; x < w; x++)
                    {
                        int s = (sy * w + x) * bytes;
                        if (bytes == 1) { row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = px[s]; row[x * 4 + 3] = 255; }
                        else { row[x * 4] = px[s]; row[x * 4 + 1] = px[s + 1]; row[x * 4 + 2] = px[s + 2]; row[x * 4 + 3] = bytes == 4 ? px[s + 3] : (byte) 255; }
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(d.Scan0.ToInt64() + (long) y * d.Stride), row.Length);
                }
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }
    }
}
