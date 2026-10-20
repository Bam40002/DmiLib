using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using StbImageSharp;

namespace DmiLib;

public class DmiIcon
{
    public int TileWidth { get; private set; }
    public int TileHeight { get; private set; }
    public ImageResult Image { get; private set; } = null!;
    public DmiState[] States { get; private set; } = null!;
    public string Version { get; private set; } = null!;

    internal DmiIcon() { }

    private static readonly byte[] _pngHeader = [ 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A ];

    public static DmiIcon CreateFromFile(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);

        if (!data.AsSpan(0, 8).SequenceEqual(_pngHeader))
        {
            throw new InvalidDataException("Unsupported file type!");
        }

        string dmiData = "";
        int stride = _pngHeader.Length;
        while (stride + 12 <= data.Length)
        {
            uint chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(stride, 4));
            string chunkType = Encoding.ASCII.GetString(data, stride + 4, 4);

            if (chunkSize > data.Length - stride - 12)
            {
                throw new InvalidDataException("Malformed PNG.");
            }

            if (chunkType == "zTXt")
            {
                ReadOnlySpan<byte> textBytes = data.AsSpan(stride + 8, (int)chunkSize);

                int descSeperator = textBytes.IndexOf((byte)0x00); 

                if (descSeperator >= 0)
                {
                    if (Encoding.Latin1.GetString(textBytes[.. descSeperator]) == "Description")
                    {
                        using MemoryStream memoryStream = new(textBytes[(descSeperator + 2)..].ToArray());
                        using ZLibStream zLibStream = new(memoryStream, CompressionMode.Decompress);
                        using StreamReader streamReader = new(zLibStream);
                        dmiData = streamReader.ReadToEnd();
                    
                        break; // we found the shit so no need to keep searching
                    }
                }
            }

            stride += (int)(chunkSize + 12); // 12 is chunk size ( 4 bytes ) chunk name ( 4 bytes ) and crc ( 4 bytes )
        }

        DmiIcon dmiIcon;
        ImageResult imageResult = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
        if (dmiData.Contains("# BEGIN DMI"))
        {
            dmiIcon = DmiIcon.CreateFromData(dmiData);
        }
        else
        {
            // pretend its a dmi file lol
            dmiIcon = new()
            {
                TileHeight = imageResult.Height,
                TileWidth = imageResult.Width,
                States = [DmiState.NoName],
                Version = "4.0",
            };
        }

        dmiIcon.Image = imageResult;

        return dmiIcon;
    }

    public static DmiIcon CreateFromData(string data)
    {
        string dmiVersion = "???";
        int dmiTileWidth = 0;
        int dmiTileHeight = 0;

        List<DmiState> states = [];
        DmiState? state = null;
        foreach (string line in data.Split("\n"))
        {
            if (line.Contains("state = \""))
            {
                if (state is not null)
                {
                    states.Add(state);
                }

                state = new()
                {
                    Name = line[9 .. (line.Length - 1)]
                };
            }
            else if (state is not null)
            {
                if (line.Contains("\tdirs = "))
                {
                    state.Dirs = int.Parse(line[8].ToString());
                }
                else if (line.Contains("\tframes = "))
                {
                    state.Frames = int.Parse(line[10 .. line.Length]);
                }
                else if (line.Contains("\tdelay = "))
                {
                    string[] splitDelays = line[9 .. line.Length].Split(',');
                    float[] delays = new float[splitDelays.Length];
                    for (int i = 0; i < splitDelays.Length; i++)
                    {
                        delays[i] = float.Parse(splitDelays[i], CultureInfo.InvariantCulture);
                    }
                    state.Delays = delays;
                }
            }
            else if (line.Contains("version = "))
            {
                dmiVersion = line[10 .. (line.Length - 1)];
            }
            else if (line.Contains("\twidth = "))
            {
                dmiTileWidth = int.Parse(line[9 .. line.Length]);
            }
            else if (line.Contains("\theight = "))
            {
                dmiTileHeight = int.Parse(line[10 .. line.Length]);
            }
            else if (line.Contains("# END DMI"))
            {
                break;
            }
        }

        // dont lose the last state!!!
        if (state is not null)
        {
            states.Add(state);
        }

        DmiIcon dmiIcon = new()
        {
            TileHeight = dmiTileHeight,
            TileWidth = dmiTileWidth,
            Version = dmiVersion,
            States = [.. states],
        };

        return dmiIcon;
    }

    public DmiState? GetStateByName(string name)
    {
        foreach (DmiState state in States)
        {
            if (name == state.Name)
            {
                return state;
            }
        }

        return DmiState.NoName;
    }
}

public class DmiState
{
    public static readonly DmiState NoName = new();
	public string Name = string.Empty;
    public int Dirs = 1;
    public int Frames = 1;
    public float[]? Delays;
}