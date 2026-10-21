using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XdsConverter
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }

    // ------------------------------------------------------------------ image view
    class ImageView : Panel
    {
        Bitmap img;
        float zoom;          // 0 = fit
        Bitmap checker;
        public event Action<int, int> PixelHover;

        public ImageView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(48, 48, 48);
            checker = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(checker))
            {
                g.Clear(Color.FromArgb(200, 200, 200));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 150, 150)))
                {
                    g.FillRectangle(b, 0, 0, 8, 8);
                    g.FillRectangle(b, 8, 8, 8, 8);
                }
            }
        }

        public Bitmap Image
        {
            get { return img; }
            set { img = value; UpdateScroll(); Invalidate(); }
        }

        public float Zoom
        {
            get { return zoom; }
            set { zoom = value; UpdateScroll(); Invalidate(); }
        }

        float Effective()
        {
            if (img == null) return 1f;
            if (zoom > 0) return zoom;
            float f = Math.Min((float) ClientSize.Width / img.Width, (float) ClientSize.Height / img.Height);
            return Math.Max(0.01f, Math.Min(f, 8f));
        }

        void UpdateScroll()
        {
            if (img != null && zoom > 0)
                AutoScrollMinSize = new Size((int) Math.Ceiling(img.Width * zoom), (int) Math.Ceiling(img.Height * zoom));
            else
                AutoScrollMinSize = Size.Empty;
        }

        RectangleF Dest()
        {
            float e = Effective();
            float w = img.Width * e, h = img.Height * e;
            float x = w < ClientSize.Width ? (ClientSize.Width - w) / 2f : AutoScrollPosition.X;
            float y = h < ClientSize.Height ? (ClientSize.Height - h) / 2f : AutoScrollPosition.Y;
            return new RectangleF(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.Clear(BackColor);
            if (img == null) return;
            RectangleF d = Dest();
            using (TextureBrush tb = new TextureBrush(checker))
            {
                tb.TranslateTransform(d.X, d.Y);
                g.FillRectangle(tb, d);
            }
            float e = Effective();
            g.InterpolationMode = e >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, d.X, d.Y, d.Width, d.Height);
            using (Pen p = new Pen(Color.FromArgb(90, 90, 90)))
                g.DrawRectangle(p, d.X - 1, d.Y - 1, d.Width + 1, d.Height + 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (img == null || PixelHover == null) return;
            RectangleF d = Dest();
            int x = (int) Math.Floor((e.X - d.X) / Effective());
            int y = (int) Math.Floor((e.Y - d.Y) / Effective());
            if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) PixelHover(-1, -1); else PixelHover(x, y);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (PixelHover != null) PixelHover(-1, -1);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    }

    // ------------------------------------------------------------------ new / convert dialog
    class NewForm : Form
    {
        NumericUpDown nw = new NumericUpDown(), nh = new NumericUpDown();
        ComboBox fmt = new ComboBox();
        CheckBox gamma = new CheckBox(), mips = new CheckBox();
        Label info = new Label();
        static readonly DxtFormat[] Codecs = { DxtFormat.Dxt1, DxtFormat.Dxt3, DxtFormat.Dxt5, DxtFormat.Dxn, DxtFormat.A8R8G8B8 };

        public int TexWidth { get { return (int) nw.Value; } }
        public int TexHeight { get { return (int) nh.Value; } }
        public DxtFormat Codec { get { return Codecs[fmt.SelectedIndex]; } }
        public bool Gamma { get { return gamma.Checked && gamma.Enabled; } }
        public bool Mips { get { return mips.Checked; } }

        public NewForm(string title, string okText, int w, int h, DxtFormat codec, bool isGamma, bool hasMips, float s)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size((int) (400 * s), (int) (232 * s));

            Label l1 = new Label(); l1.Text = "Width:"; l1.SetBounds((int) (14 * s), (int) (18 * s), (int) (60 * s), (int) (20 * s));
            nw.SetBounds((int) (80 * s), (int) (14 * s), (int) (80 * s), (int) (24 * s));
            Label l2 = new Label(); l2.Text = "Height:"; l2.SetBounds((int) (190 * s), (int) (18 * s), (int) (60 * s), (int) (20 * s));
            nh.SetBounds((int) (256 * s), (int) (14 * s), (int) (80 * s), (int) (24 * s));
            foreach (NumericUpDown n in new NumericUpDown[] { nw, nh })
            {
                n.Minimum = 1; n.Maximum = 8192; n.ThousandsSeparator = false;
                n.ValueChanged += delegate { UpdateInfo(); };
            }
            nw.Value = Math.Max(1, Math.Min(8192, w));
            nh.Value = Math.Max(1, Math.Min(8192, h));

            Label l3 = new Label(); l3.Text = "Pixel format:"; l3.SetBounds((int) (14 * s), (int) (56 * s), (int) (90 * s), (int) (20 * s));
            fmt.DropDownStyle = ComboBoxStyle.DropDownList;
            fmt.SetBounds((int) (14 * s), (int) (78 * s), (int) (372 * s), (int) (24 * s));
            fmt.Items.AddRange(new object[] {
                "DXT1  -  opaque / 1-bit alpha (smallest)",
                "DXT3  -  sharp alpha",
                "DXT5  -  smooth alpha (most used)",
                "DXN  -  normal map (2 channels)",
                "A8R8G8B8  -  uncompressed 32-bit" });
            int idx = Array.IndexOf(Codecs, codec);
            fmt.SelectedIndex = idx < 0 ? 2 : idx;
            fmt.SelectedIndexChanged += delegate { gamma.Enabled = Codec != DxtFormat.Dxn; UpdateInfo(); };

            gamma.Text = "sRGB / gamma color (use for diffuse textures)";
            gamma.SetBounds((int) (14 * s), (int) (112 * s), (int) (372 * s), (int) (22 * s));
            gamma.Checked = isGamma;
            gamma.Enabled = Codec != DxtFormat.Dxn;
            gamma.CheckedChanged += delegate { UpdateInfo(); };
            mips.Text = "Generate mipmaps";
            mips.SetBounds((int) (14 * s), (int) (136 * s), (int) (372 * s), (int) (22 * s));
            mips.Checked = hasMips;
            mips.CheckedChanged += delegate { UpdateInfo(); };

            info.SetBounds((int) (14 * s), (int) (166 * s), (int) (372 * s), (int) (20 * s));
            info.ForeColor = SystemColors.GrayText;

            Button ok = new Button(); ok.Text = okText; ok.DialogResult = DialogResult.OK;
            ok.SetBounds((int) (216 * s), (int) (192 * s), (int) (84 * s), (int) (28 * s));
            Button ca = new Button(); ca.Text = "Cancel"; ca.DialogResult = DialogResult.Cancel;
            ca.SetBounds((int) (306 * s), (int) (192 * s), (int) (84 * s), (int) (28 * s));
            AcceptButton = ok; CancelButton = ca;
            Controls.AddRange(new Control[] { l1, nw, l2, nh, l3, fmt, gamma, mips, info, ok, ca });
            UpdateInfo();
        }

        void UpdateInfo()
        {
            try
            {
                XdsTexture t = XdsTexture.Create(TexWidth, TexHeight, Codec, Gamma, Mips);
                info.Text = "Mip levels: " + t.LevelCount + "     File size: " + (XdsTexture.HeaderSize + t.Data.Length).ToString("N0") + " bytes";
            }
            catch (Exception ex) { info.Text = ex.Message; }
        }
    }

    // ------------------------------------------------------------------ main window
    class MainForm : Form
    {
        const string AppName = "XDS Converter";

        XdsTexture tex;
        string path;
        bool modified;
        bool bakDone;
        float S;
        string texconv;

        ImageView view;
        ListView props;
        ToolStrip ts;
        MenuStrip ms;
        StatusStrip ss;
        ToolStripStatusLabel stLeft, stRight;
        ComboBox cmbMip, cmbChan, cmbZoom;
        Label hint;
        Bitmap shown;
        ToolStripItem tbSave, tbImport, tbExport, tbConvert;
        ToolStripMenuItem mSave, mSaveAs, mImport, mExport, mConvert;

        public MainForm(string openPath)
        {
            using (Graphics g = CreateGraphics()) S = g.DpiX / 96f;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size((int) (980 * S), (int) (620 * S));
            MinimumSize = new Size((int) (720 * S), (int) (420 * S));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop;

            BuildUi();
            UpdateUi();
            Shown += delegate
            {
                try { texconv = ExtractTexconv(); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "texconv.exe is not available:\n\n" + ex.Message + "\n\nOpening and exporting still work, but importing images does not.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (openPath != null && File.Exists(openPath)) OpenFile(openPath);
            };
            FormClosing += delegate(object o, FormClosingEventArgs e) { if (!ConfirmDiscard()) e.Cancel = true; };
        }

        static string ExtractTexconv()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "texconv.exe");
            if (File.Exists(local)) return local;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("texconv.exe"))
            {
                if (s == null) throw new FileNotFoundException("texconv.exe not found (neither next to the exe nor embedded).");
                string dir = Path.Combine(Path.GetTempPath(), "XdsConverter");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "texconv.exe");
                if (!File.Exists(p) || new FileInfo(p).Length != s.Length)
                {
                    try { using (FileStream fs = new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.Read)) s.CopyTo(fs); }
                    catch (IOException) { }
                }
                return p;
            }
        }

        // ---------------------------------------------------------------- UI construction
        static bool HasFont(string name)
        {
            foreach (FontFamily f in FontFamily.Families) if (f.Name == name) return true;
            return false;
        }

        static Bitmap Glyph(string glyph, Color c, int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                using (Font f = new Font("Segoe MDL2 Assets", size * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(c))
                {
                    SizeF sz = g.MeasureString(glyph, f);
                    g.DrawString(glyph, f, b, (size - sz.Width) / 2f, (size - sz.Height) / 2f);
                }
            }
            return bmp;
        }

        ToolStripButton Btn(string text, string glyph, Color col, string tip, EventHandler click)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.TextImageRelation = TextImageRelation.ImageAboveText;
            b.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            b.ToolTipText = tip;
            b.AutoSize = false;
            b.Size = new Size((int) (70 * S), (int) (62 * S));
            if (HasFont("Segoe MDL2 Assets")) b.Image = Glyph(glyph, col, (int) (30 * S));
            b.Click += click;
            return b;
        }

        ToolStripMenuItem Mi(string text, Keys keys, EventHandler click)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text);
            if (keys != Keys.None) m.ShortcutKeys = keys;
            m.Click += click;
            return m;
        }

        ToolStripControlHost Host(string label, ComboBox cb, int width)
        {
            Panel p = new Panel();
            Label l = new Label();
            l.Text = label; l.AutoSize = true;
            l.Location = new Point(0, (int) (3 * S));
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.Width = (int) (width * S);
            cb.Location = new Point(0, (int) (22 * S));
            p.Controls.Add(l); p.Controls.Add(cb);
            p.Size = new Size((int) ((width + 8) * S), (int) (50 * S));
            return new ToolStripControlHost(p);
        }

        void BuildUi()
        {
            view = new ImageView();
            view.Dock = DockStyle.Fill;
            view.AllowDrop = true;
            view.DragEnter += OnDragEnter; view.DragDrop += OnDragDrop;
            view.PixelHover += OnPixelHover;

            hint = new Label();
            hint.Text = "No texture open.\n\nUse File > Open (Ctrl+O), File > New (Ctrl+N)\nor drag an .xds file (or an image) here.";
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.ForeColor = Color.FromArgb(170, 170, 170);
            hint.BackColor = Color.Transparent;
            hint.Dock = DockStyle.Fill;
            hint.AllowDrop = true;
            hint.DragEnter += OnDragEnter; hint.DragDrop += OnDragDrop;
            view.Controls.Add(hint);

            props = new ListView();
            props.Dock = DockStyle.Right;
            props.Width = (int) (260 * S);
            props.View = View.Details;
            props.FullRowSelect = true;
            props.HeaderStyle = ColumnHeaderStyle.None;
            props.Columns.Add("Property", (int) (100 * S));
            props.Columns.Add("Value", (int) (150 * S));
            props.GridLines = false;

            ss = new StatusStrip();
            stLeft = new ToolStripStatusLabel(); stLeft.Spring = true; stLeft.TextAlign = ContentAlignment.MiddleLeft;
            stRight = new ToolStripStatusLabel(); stRight.TextAlign = ContentAlignment.MiddleRight;
            ss.Items.Add(stLeft); ss.Items.Add(stRight);

            cmbMip = new ComboBox(); cmbChan = new ComboBox(); cmbZoom = new ComboBox();
            cmbChan.Items.AddRange(new object[] { "RGBA", "RGB (no alpha)", "Alpha", "Red", "Green", "Blue" });
            cmbChan.SelectedIndex = 0;
            cmbZoom.Items.AddRange(new object[] { "Fit", "25%", "50%", "100%", "200%", "400%", "800%" });
            cmbZoom.SelectedIndex = 0;
            cmbMip.SelectedIndexChanged += delegate { RenderView(); };
            cmbChan.SelectedIndexChanged += delegate { RenderView(); };
            cmbZoom.SelectedIndexChanged += delegate
            {
                float[] z = { 0, 0.25f, 0.5f, 1, 2, 4, 8 };
                view.Zoom = z[cmbZoom.SelectedIndex];
            };

            ts = new ToolStrip();
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.ImageScalingSize = new Size((int) (30 * S), (int) (30 * S));
            ts.Items.Add(Btn("New", "", Color.FromArgb(90, 90, 160), "New texture (Ctrl+N)", delegate { DoNew(false); }));
            ts.Items.Add(Btn("Open", "", Color.FromArgb(220, 160, 30), "Open .xds (Ctrl+O)", delegate { DoOpen(); }));
            tbSave = Btn("Save", "", Color.FromArgb(30, 110, 200), "Save (Ctrl+S)", delegate { DoSave(false); });
            ts.Items.Add(tbSave);
            ts.Items.Add(new ToolStripSeparator());
            tbImport = Btn("Import", "", Color.FromArgb(40, 150, 60), "Import an image into this texture (Ctrl+I)", delegate { DoImport(); });
            tbExport = Btn("Export", "", Color.FromArgb(40, 150, 60), "Export the current mip level as an image (Ctrl+E)", delegate { DoExport(); });
            tbConvert = Btn("Convert", "", Color.FromArgb(200, 110, 30), "Change format / size of this texture", delegate { DoConvert(); });
            ts.Items.Add(tbImport); ts.Items.Add(tbExport); ts.Items.Add(tbConvert);
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Host("Mip level", cmbMip, 120));
            ts.Items.Add(Host("Channels", cmbChan, 120));
            ts.Items.Add(Host("Zoom", cmbZoom, 80));

            ms = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            mSave = Mi("&Save", Keys.Control | Keys.S, delegate { DoSave(false); });
            mSaveAs = Mi("Save &as...", Keys.Control | Keys.Shift | Keys.S, delegate { DoSave(true); });
            file.DropDownItems.Add(Mi("&New...", Keys.Control | Keys.N, delegate { DoNew(false); }));
            file.DropDownItems.Add(Mi("New from &image...", Keys.Control | Keys.Shift | Keys.N, delegate { DoNew(true); }));
            file.DropDownItems.Add(Mi("&Open...", Keys.Control | Keys.O, delegate { DoOpen(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(mSave); file.DropDownItems.Add(mSaveAs);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Mi("E&xit", Keys.Alt | Keys.F4, delegate { Close(); }));
            ToolStripMenuItem tm = new ToolStripMenuItem("&Texture");
            mImport = Mi("&Import image...", Keys.Control | Keys.I, delegate { DoImport(); });
            mExport = Mi("&Export image...", Keys.Control | Keys.E, delegate { DoExport(); });
            mConvert = Mi("&Change format / size...", Keys.Control | Keys.T, delegate { DoConvert(); });
            tm.DropDownItems.AddRange(new ToolStripItem[] { mImport, mExport, new ToolStripSeparator(), mConvert });
            ToolStripMenuItem help = new ToolStripMenuItem("&?");
            help.DropDownItems.Add(Mi("&About " + AppName, Keys.None, delegate
            {
                MessageBox.Show(this, AppName + "\n\nOpen, import, export and create Forza Motorsport 4 .xds textures.\n\n" +
                    "Formats: DXT1, DXT3, DXT5, DXN, A8R8G8B8 (Xbox 360 tiled layout).\n" +
                    "Import accepts PNG, TGA, BMP, JPG and DDS; mipmaps are generated automatically.\n" +
                    "Export writes PNG, TGA or BMP with the stored values (no gamma conversion).\n\n" +
                    "Block compression by texconv (DirectXTex, Microsoft, MIT).\n" +
                    "Xbox 360 layout code derived from Forza Studio (Mike Davis, MIT).",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
            ms.Items.AddRange(new ToolStripItem[] { file, tm, help });
            MainMenuStrip = ms;

            Controls.Add(view);
            Controls.Add(props);
            Controls.Add(ss);
            Controls.Add(ts);
            Controls.Add(ms);
        }

        // ---------------------------------------------------------------- state
        void UpdateUi()
        {
            bool has = tex != null;
            foreach (ToolStripItem t in new ToolStripItem[] { tbSave, mSave, mSaveAs, tbImport, mImport, tbExport, mExport, tbConvert, mConvert }) t.Enabled = has;
            cmbMip.Enabled = cmbChan.Enabled = cmbZoom.Enabled = has;
            hint.Visible = !has;
            string name = has ? (path == null ? "(untitled).xds" : Path.GetFileName(path)) : null;
            Text = has ? name + (modified ? "*" : "") + " - " + AppName : AppName;
            stLeft.Text = has ? (path ?? "Not saved yet") : "";
            stRight.Text = "";

            props.Items.Clear();
            if (has)
            {
                AddProp("File", name);
                AddProp("Size", tex.Width + " x " + tex.Height);
                AddProp("Format", XdsTexture.FormatName(tex.Codec) + "  (type " + tex.Format + ")");
                AddProp("Color", tex.IsGamma ? "sRGB (gamma)" : "Linear");
                AddProp("Mip levels", tex.LevelCount.ToString());
                AddProp("Layout", tex.Tiled ? "Tiled (Xbox 360)" : "Linear");
                AddProp("Endian swap", tex.Endian.ToString());
                AddProp("File size", (XdsTexture.HeaderSize + tex.Data.Length).ToString("N0") + " bytes");
            }
        }

        void AddProp(string k, string v)
        {
            ListViewItem i = new ListViewItem(k);
            i.SubItems.Add(v);
            props.Items.Add(i);
        }

        void RebuildMips()
        {
            int keep = Math.Max(0, cmbMip.SelectedIndex);
            cmbMip.Items.Clear();
            if (tex != null)
            {
                for (int l = 0; l < tex.LevelCount; l++)
                {
                    XdsTexture.Level info = tex.GetLevel(l);
                    cmbMip.Items.Add(l + "  (" + info.Width + "x" + info.Height + ")");
                }
                cmbMip.SelectedIndex = Math.Min(keep, cmbMip.Items.Count - 1);
            }
        }

        // current level as ARGB, exactly as stored
        Bitmap CurrentLevel()
        {
            return tex.GetLevelBitmap(Math.Max(0, cmbMip.SelectedIndex));
        }

        void RenderView()
        {
            if (tex == null || cmbMip.SelectedIndex < 0) { view.Image = null; return; }
            Bitmap raw = CurrentLevel();
            int mode = cmbChan.SelectedIndex;
            if (mode > 0)
            {
                Rectangle r = new Rectangle(0, 0, raw.Width, raw.Height);
                BitmapData d = raw.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                byte[] px = new byte[d.Stride * raw.Height];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                for (int y = 0; y < raw.Height; y++)
                    for (int x = 0; x < raw.Width; x++)
                    {
                        int o = y * d.Stride + x * 4;      // B G R A
                        byte b = px[o], g = px[o + 1], rr = px[o + 2], a = px[o + 3];
                        switch (mode)
                        {
                            case 1: px[o + 3] = 255; break;
                            case 2: px[o] = px[o + 1] = px[o + 2] = a; px[o + 3] = 255; break;
                            case 3: px[o] = px[o + 1] = px[o + 2] = rr; px[o + 3] = 255; break;
                            case 4: px[o] = px[o + 1] = px[o + 2] = g; px[o + 3] = 255; break;
                            case 5: px[o] = px[o + 1] = px[o + 2] = b; px[o + 3] = 255; break;
                        }
                    }
                Marshal.Copy(px, 0, d.Scan0, px.Length);
                raw.UnlockBits(d);
            }
            Bitmap old = shown;
            shown = raw;
            view.Image = shown;
            if (old != null) old.Dispose();
        }

        void OnPixelHover(int x, int y)
        {
            if (shown == null || x < 0) { stRight.Text = ""; return; }
            Color c = shown.GetPixel(x, y);
            stRight.Text = "x=" + x + " y=" + y + "   R=" + c.R + " G=" + c.G + " B=" + c.B + " A=" + c.A;
        }

        void SetTexture(XdsTexture t, string p, bool isModified)
        {
            tex = t; path = p; modified = isModified; bakDone = false;
            cmbMip.Items.Clear();
            RebuildMips();
            RenderView();
            UpdateUi();
        }

        bool ConfirmDiscard()
        {
            if (tex == null || !modified) return true;
            DialogResult r = MessageBox.Show(this, "The texture has unsaved changes. Save them?", AppName,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return DoSave(false);
            return true;
        }

        void Err(Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        bool NeedTexconv()
        {
            if (texconv != null && File.Exists(texconv)) return true;
            MessageBox.Show(this, "texconv.exe is not available, so images cannot be compressed.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        const string ImageFilter = "Images (*.png;*.tga;*.bmp;*.jpg;*.jpeg;*.dds)|*.png;*.tga;*.bmp;*.jpg;*.jpeg;*.dds|All files (*.*)|*.*";

        // ---------------------------------------------------------------- commands
        void DoOpen()
        {
            if (!ConfirmDiscard()) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Open texture";
                d.Filter = "XDS textures (*.xds)|*.xds|All files (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
            }
        }

        void OpenFile(string p)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                SetTexture(XdsTexture.FromFile(p), p, false);
            }
            catch (Exception ex) { Err(ex); }
            finally { Cursor = Cursors.Default; }
        }

        void DoNew(bool fromImage)
        {
            if (!ConfirmDiscard()) return;
            string img = null;
            int w = 512, h = 512;
            if (fromImage)
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Choose the source image";
                    d.Filter = ImageFilter;
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    img = d.FileName;
                }
                try { Size sz = XdsTexture.ImageSize(img); w = sz.Width; h = sz.Height; } catch (Exception ex) { Err(ex); return; }
            }
            if (fromImage && !NeedTexconv()) return;
            using (NewForm f = new NewForm(fromImage ? "New texture from image" : "New texture", "Create", w, h, DxtFormat.Dxt5, true, true, S))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    XdsTexture t = XdsTexture.Create(f.TexWidth, f.TexHeight, f.Codec, f.Gamma, f.Mips);
                    if (img != null) t.ImportFile(img, texconv, true);
                    SetTexture(t, null, true);
                }
                catch (Exception ex) { Err(ex); }
                finally { Cursor = Cursors.Default; }
            }
        }

        bool DoSave(bool saveAs)
        {
            if (tex == null) return false;
            string target = path;
            if (target == null || saveAs)
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Title = "Save texture";
                    d.Filter = "XDS textures (*.xds)|*.xds";
                    d.DefaultExt = "xds";
                    d.FileName = path != null ? Path.GetFileName(path) : "texture.xds";
                    if (path != null) d.InitialDirectory = Path.GetDirectoryName(path);
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    target = d.FileName;
                }
            }
            try
            {
                // keep a .bak of the original the first time an existing file is overwritten
                if (File.Exists(target) && !(bakDone && target == path) && !File.Exists(target + ".bak"))
                    File.Copy(target, target + ".bak");
                File.WriteAllBytes(target, tex.ToBytes());
                path = target; modified = false; bakDone = true;
                UpdateUi();
                return true;
            }
            catch (Exception ex) { Err(ex); return false; }
        }

        void DoImport()
        {
            if (tex == null) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Import image";
                d.Filter = ImageFilter;
                if (d.ShowDialog(this) == DialogResult.OK) ImportImage(d.FileName);
            }
        }

        void ImportImage(string file)
        {
            if (tex == null || !NeedTexconv()) return;
            try
            {
                Size sz = XdsTexture.ImageSize(file);
                bool resize = false;
                if (sz.Width != tex.Width || sz.Height != tex.Height)
                {
                    if (MessageBox.Show(this, "The image is " + sz.Width + "x" + sz.Height + " but the texture is " + tex.Width + "x" + tex.Height +
                        ".\nScale the image to the texture size?", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    resize = true;
                }
                Cursor = Cursors.WaitCursor;
                tex.ImportFile(file, texconv, resize);
                modified = true;
                RenderView();
                UpdateUi();
            }
            catch (Exception ex) { Err(ex); }
            finally { Cursor = Cursors.Default; }
        }

        void DoExport()
        {
            if (tex == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Export image";
                d.Filter = "PNG (*.png)|*.png|TGA (*.tga)|*.tga|BMP (*.bmp)|*.bmp";
                d.DefaultExt = "png";
                d.FileName = (path != null ? Path.GetFileNameWithoutExtension(path) : "texture") + (cmbMip.SelectedIndex > 0 ? "_mip" + cmbMip.SelectedIndex : "");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (Bitmap b = CurrentLevel()) XdsTexture.SaveBitmap(b, d.FileName);
                }
                catch (Exception ex) { Err(ex); }
            }
        }

        void DoConvert()
        {
            if (tex == null || !NeedTexconv()) return;
            using (NewForm f = new NewForm("Change format / size", "Convert", tex.Width, tex.Height, tex.Codec, tex.IsGamma, tex.LevelCount > 1, S))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string tmp = Path.Combine(Path.GetTempPath(), "xdsconv_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    Cursor = Cursors.WaitCursor;
                    using (Bitmap b = tex.GetLevelBitmap(0)) b.Save(tmp, ImageFormat.Png);
                    XdsTexture t = XdsTexture.Create(f.TexWidth, f.TexHeight, f.Codec, f.Gamma, f.Mips);
                    t.ImportFile(tmp, texconv, true);
                    SetTexture(t, path, true);
                }
                catch (Exception ex) { Err(ex); }
                finally { Cursor = Cursors.Default; try { File.Delete(tmp); } catch { } }
            }
        }

        // ---------------------------------------------------------------- drag & drop
        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            string f = files[0];
            BeginInvoke(new Action(delegate
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".xds") { if (ConfirmDiscard()) OpenFile(f); }
                else if (tex != null) ImportImage(f);
                else MessageBox.Show(this, "Open or create a texture first, then drop the image to import it.", AppName);
            }));
        }
    }
}
