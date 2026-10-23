using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

// Crée un .zip lisible par macOS qui conserve les droits Unix (exécutable pour Contents/MacOS/*).
// Le zip intégré à .NET sous Windows ne sait pas les écrire : sans eux, le jeu ne se lance pas sur Mac.
public static class MacZip
{
    const uint ModeExecutable = 0x81ED; // fichier -rwxr-xr-x
    const uint ModeRegular = 0x81A4;    // fichier -rw-r--r--
    const ushort MadeByUnix = 0x0314;   // hôte Unix, format zip 2.0
    const ushort Utf8Names = 0x0800;
    const ushort DosDate1980 = 0x21;    // 1er janvier 1980 (date sans importance)

    struct Entry
    {
        public byte[] Name;
        public uint Crc, CompressedSize, Size, Offset, Mode;
        public ushort Method;
    }

    // extraFiles : fichiers ajoutés à la racine du zip (nom -> (contenu, exécutable))
    public static void Create(string appPath, string zipPath, Dictionary<string, (string content, bool executable)> extraFiles)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(appPath));
        var entries = new List<Entry>();

        using (var stream = File.Create(zipPath))
        using (var writer = new BinaryWriter(stream))
        {
            foreach (string file in Directory.GetFiles(appPath, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFullPath(file).Substring(root.Length + 1).Replace('\\', '/');
                bool executable = name.Contains(".app/Contents/MacOS/");
                entries.Add(WriteFile(writer, name, File.ReadAllBytes(file), executable ? ModeExecutable : ModeRegular));
            }
            foreach (var extra in extraFiles)
            {
                byte[] content = Encoding.UTF8.GetBytes(extra.Value.content);
                entries.Add(WriteFile(writer, extra.Key, content, extra.Value.executable ? ModeExecutable : ModeRegular));
            }

            // Répertoire central
            uint centralStart = (uint)stream.Position;
            foreach (Entry e in entries)
            {
                writer.Write(0x02014b50u);
                writer.Write(MadeByUnix);
                writer.Write((ushort)20);
                writer.Write(Utf8Names);
                writer.Write(e.Method);
                writer.Write((ushort)0);
                writer.Write(DosDate1980);
                writer.Write(e.Crc);
                writer.Write(e.CompressedSize);
                writer.Write(e.Size);
                writer.Write((ushort)e.Name.Length);
                writer.Write((ushort)0); // extra
                writer.Write((ushort)0); // commentaire
                writer.Write((ushort)0); // disque
                writer.Write((ushort)0); // attributs internes
                writer.Write(e.Mode << 16); // attributs externes = droits Unix
                writer.Write(e.Offset);
                writer.Write(e.Name);
            }
            uint centralSize = (uint)stream.Position - centralStart;

            writer.Write(0x06054b50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)entries.Count);
            writer.Write((ushort)entries.Count);
            writer.Write(centralSize);
            writer.Write(centralStart);
            writer.Write((ushort)0);
        }
    }

    static Entry WriteFile(BinaryWriter writer, string name, byte[] data, uint mode)
    {
        byte[] compressed = Deflate(data);
        ushort method = 8;
        if (compressed.Length >= data.Length)
        {
            compressed = data;
            method = 0; // stocké tel quel
        }

        var entry = new Entry
        {
            Name = Encoding.UTF8.GetBytes(name),
            Crc = Crc32(data),
            CompressedSize = (uint)compressed.Length,
            Size = (uint)data.Length,
            Offset = (uint)writer.BaseStream.Position,
            Mode = mode,
            Method = method,
        };

        writer.Write(0x04034b50u);
        writer.Write((ushort)20);
        writer.Write(Utf8Names);
        writer.Write(entry.Method);
        writer.Write((ushort)0);
        writer.Write(DosDate1980);
        writer.Write(entry.Crc);
        writer.Write(entry.CompressedSize);
        writer.Write(entry.Size);
        writer.Write((ushort)entry.Name.Length);
        writer.Write((ushort)0);
        writer.Write(entry.Name);
        writer.Write(compressed);
        return entry;
    }

    static byte[] Deflate(byte[] data)
    {
        using (var output = new MemoryStream())
        {
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                deflate.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }

    static uint[] crcTable;

    static uint Crc32(byte[] data)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                crcTable[i] = c;
            }
        }

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
