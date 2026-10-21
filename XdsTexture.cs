namespace XdsConverter
{
    using System;
    using System.IO;

    // .xds = 52 byte big endian D3DBaseTexture header (the last 6 dwords are the Xenos
    // texture fetch constant) followed by texture memory: base level at BasePage and
    // mips at MipPage (4 KiB pages). Layout rules follow Xenia's texture_util.cc.
    public partial class XdsTexture
    {
        public const int HeaderSize = 52;
        private const int PageSize = 4096;
        private const int TileBlocks = 32;

        public uint[] Header = new uint[13];
        public byte[] Data;

        private XdsTexture()
        {
        }

        public XdsTexture(byte[] file)
        {
            if (file == null || file.Length < HeaderSize)
            {
                throw new InvalidDataException("File too small to be an XDS texture.");
            }
            for (int i = 0; i < 13; i++)
            {
                this.Header[i] = (uint) ((file[i * 4] << 24) | (file[(i * 4) + 1] << 16) | (file[(i * 4) + 2] << 8) | file[(i * 4) + 3]);
            }
            if (this.Header[0] != 3 || this.Header[1] != 1)
            {
                throw new InvalidDataException("Not a valid XDS file.");
            }
            this.Data = new byte[file.Length - HeaderSize];
            Buffer.BlockCopy(file, HeaderSize, this.Data, 0, this.Data.Length);
            if (this.Dimension != 1 || this.Codec == DxtFormat.Unknown)
            {
                throw new NotSupportedException(string.Format("Unsupported XDS texture (dimension {0}, format 0x{1:X2}).", this.Dimension, this.Format));
            }
        }

        public static XdsTexture FromFile(string path)
        {
            return new XdsTexture(File.ReadAllBytes(path));
        }

        public byte[] ToBytes()
        {
            byte[] file = new byte[HeaderSize + this.Data.Length];
            for (int i = 0; i < 13; i++)
            {
                uint v = this.Header[i];
                file[i * 4] = (byte) (v >> 24);
                file[(i * 4) + 1] = (byte) (v >> 16);
                file[(i * 4) + 2] = (byte) (v >> 8);
                file[(i * 4) + 3] = (byte) v;
            }
            Buffer.BlockCopy(this.Data, 0, file, HeaderSize, this.Data.Length);
            return file;
        }

        private uint Fetch(int dword)
        {
            return this.Header[7 + dword];
        }

        public bool Tiled { get { return (this.Fetch(0) >> 31) != 0; } }
        public int PitchTexels { get { return (int) ((this.Fetch(0) >> 22) & 0x1FF) << 5; } }
        public bool IsGamma { get { return ((this.Fetch(0) >> 2) & 3) == 3; } }
        public int Format { get { return (int) (this.Fetch(1) & 0x3F); } }
        public int Endian { get { return (int) ((this.Fetch(1) >> 6) & 3); } }
        public int BasePage { get { return (int) (this.Fetch(1) >> 12); } }
        public int Width { get { return (int) (this.Fetch(2) & 0x1FFF) + 1; } }
        public int Height { get { return (int) ((this.Fetch(2) >> 13) & 0x1FFF) + 1; } }
        public int MipMaxLevel { get { return (int) ((this.Fetch(4) >> 6) & 0xF); } }
        public int Dimension { get { return (int) ((this.Fetch(5) >> 9) & 3); } }
        public bool PackedMips { get { return ((this.Fetch(5) >> 11) & 1) != 0; } }
        public int MipPage { get { return (int) (this.Fetch(5) >> 12); } }

        public DxtFormat Codec
        {
            get
            {
                switch (this.Format)
                {
                    case 6:
                    case 50: return DxtFormat.A8R8G8B8;
                    case 18:
                    case 51: return DxtFormat.Dxt1;
                    case 19:
                    case 52: return DxtFormat.Dxt3;
                    case 20:
                    case 53: return DxtFormat.Dxt5;
                    case 49: return DxtFormat.Dxn;
                    case 59: return DxtFormat.Dxt5A;
                }
                return DxtFormat.Unknown;
            }
        }

        public int BytesPerBlock { get { return Dxt.BytesPerBlock(this.Codec); } }
        public int BlockSize { get { return (this.Codec == DxtFormat.A8R8G8B8) ? 1 : 4; } }

        // Mip page 0 means "no mips", except when the whole chain is packed in the base tail
        // (small textures), where the mips share the base page.
        public int LevelCount
        {
            get
            {
                if (this.MipPage == 0 && !this.MipsInBaseTail)
                {
                    return 1;
                }
                return Math.Min(this.MipMaxLevel, Log2Floor(Math.Max(this.Width, this.Height))) + 1;
            }
        }

        internal bool MipsInBaseTail
        {
            get { return this.MipPage == 0 && this.PackedLevel == 0; }
        }

        public override string ToString()
        {
            return string.Format("{0}x{1} {2}{3} levels={4} packed={5} tiled={6}", this.Width, this.Height, this.Codec, this.IsGamma ? " gamma" : "", this.LevelCount, this.PackedMips, this.Tiled);
        }

        internal static int DivUp(int value, int divisor) { return (value + divisor - 1) / divisor; }
        internal static int Align(int value, int alignment) { return DivUp(value, alignment) * alignment; }

        internal static int Log2Floor(int value)
        {
            int log = 0;
            while ((1 << (log + 1)) <= value)
            {
                log++;
            }
            return log;
        }

        internal static int Log2Ceil(int value)
        {
            int log = 0;
            while ((1 << log) < value)
            {
                log++;
            }
            return log;
        }

        internal static int NextPow2(int value) { return 1 << Log2Ceil(value); }
    }
}

