using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;

namespace edds2image
{
	internal class Program
	{
		[STAThread]
		static void Main(string[] args)
		{
			string directoryPath;
			string[] eddsFiles;

			if (args.Length > 0 && File.Exists(args[0]))
			{
				directoryPath = Path.GetDirectoryName(args[0])!;
				eddsFiles = Directory.GetFiles(directoryPath, "*.edds");
			}
			else
			{
				directoryPath = Directory.GetCurrentDirectory();
				eddsFiles = Directory.GetFiles(directoryPath, "*.edds");

				if (eddsFiles.Length == 0)
				{
					Console.WriteLine("No .edds files found in the directory.");
					Console.WriteLine("Please select an .edds file manually...");

					string? selectedFile = ShowDialog();
					if (!string.IsNullOrEmpty(selectedFile))
					{
						directoryPath = Path.GetDirectoryName(selectedFile)!;
						eddsFiles = Directory.GetFiles(directoryPath, "*.edds");
					}
					else
					{
						Console.WriteLine("Operation cancelled. No file selected.");
						Console.WriteLine("Press any key to exit.");
						Console.ReadKey();
						return;
					}
				}
			}

			string[] folders = { "dds", "png", "tif" };
			foreach (var f in folders)
			{
				Directory.CreateDirectory(Path.Combine(directoryPath, f));
			}

			Console.WriteLine($"Starting processing of {eddsFiles.Length} files in: {directoryPath}");

			Parallel.ForEach(eddsFiles, eddsPath =>
			{
				string fileName = Path.GetFileNameWithoutExtension(eddsPath);
				try
				{
					byte[] ddsData = ConvertEddsToDdsBuffer(eddsPath);

					File.WriteAllBytes(Path.Combine(directoryPath, "dds", fileName + ".dds"), ddsData);

					SaveAsImage(ddsData, Path.Combine(directoryPath, "png", fileName + ".png"), ImageFormat.Png);
					SaveAsImage(ddsData, Path.Combine(directoryPath, "tif", fileName + ".tif"), ImageFormat.Tiff);

					Console.WriteLine($"[OK] {fileName}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[ERROR] {fileName}: {ex.Message}");
				}
			});

			Console.WriteLine("\nDone! Press any key to exit.");
			Console.ReadKey();
		}

		private static string? ShowDialog()
		{
			string? selectedPath = null;
			Thread thread = new Thread(() =>
			{
				using (OpenFileDialog openFileDialog = new OpenFileDialog())
				{
					openFileDialog.Filter = "EDDS files (*.edds)|*.edds";
					openFileDialog.Title = "Select an .edds file to start batch conversion";
					openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();

					if (openFileDialog.ShowDialog() == DialogResult.OK)
					{
						selectedPath = openFileDialog.FileName;
					}
				}
			});

			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
			return selectedPath;
		}

		private static byte[] ConvertEddsToDdsBuffer(string eddsPath)
		{
			using var reader = new BinaryReader(File.Open(eddsPath, FileMode.Open, FileAccess.Read, FileShare.Read));
			byte[] ddsHeader = reader.ReadBytes(128);
			if (ddsHeader.Length < 128) throw new Exception("Invalid DDS header.");

			int height = BitConverter.ToInt32(ddsHeader, 12);
			int width = BitConverter.ToInt32(ddsHeader, 16);
			int mipCount = Math.Max(1, BitConverter.ToInt32(ddsHeader, 28));

			byte[]? ddsHeaderDx10 = null;
			int bytesPerBlock = 16;

			if (ddsHeader[84] == 'D' && ddsHeader[85] == 'X' && ddsHeader[86] == '1' && ddsHeader[87] == '0')
			{
				ddsHeaderDx10 = reader.ReadBytes(20);
				uint dxgiFormat = BitConverter.ToUInt32(ddsHeaderDx10, 0);
				if (dxgiFormat >= 70 && dxgiFormat <= 72) bytesPerBlock = 8;
			}
			else if (Encoding.UTF8.GetString(ddsHeader, 84, 4) == "DXT1")
			{
				bytesPerBlock = 8;
			}

			var blockInfos = new List<(string Tag, int Size)>();
			while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
			{
				string tag = Encoding.UTF8.GetString(reader.ReadBytes(4));
				int size = reader.ReadInt32();
				if (tag == "COPY" || tag == "LZ4 ") blockInfos.Add((tag, size));
				else { reader.BaseStream.Seek(-8, SeekOrigin.Current); break; }
			}

			var pixelData = new List<byte>();
			for (int i = 0; i < blockInfos.Count; i++)
			{
				var block = blockInfos[i];
				int mipLevel = mipCount - 1 - i;
				int mipW = Math.Max(1, width >> mipLevel);
				int mipH = Math.Max(1, height >> mipLevel);
				int expectedSize = ((mipW + 3) / 4) * ((mipH + 3) / 4) * bytesPerBlock;

				byte[] decoded;
				if (block.Tag == "COPY")
				{
					decoded = reader.ReadBytes(block.Size);
				}
				else
				{
					uint totalDecompressedSize = reader.ReadUInt32();
					byte[] target = new byte[totalDecompressedSize];
					using var decoder = new K4os.Compression.LZ4.Encoders.LZ4ChainDecoder(65536, 0);
					int processed = 4;
					int currentPos = 0;

					try
					{
						while (processed < block.Size)
						{
							int bSize = reader.ReadInt32() & int.MaxValue;
							processed += 4;
							byte[] compressed = reader.ReadBytes(bSize);
							processed += bSize;

							byte[] tmp = new byte[65536];
							K4os.Compression.LZ4.Encoders.LZ4EncoderExtensions.DecodeAndDrain(decoder, compressed, 0, bSize, tmp, 0, 65536, out int decodedCount);
							Array.Copy(tmp, 0, target, currentPos, Math.Min(decodedCount, (int)totalDecompressedSize - currentPos));
							currentPos += decodedCount;
						}
						decoded = target;
					}
					catch
					{
						reader.BaseStream.Seek(-processed, SeekOrigin.Current);
						byte[] raw = reader.ReadBytes(block.Size);
						decoded = new byte[expectedSize];
						K4os.Compression.LZ4.LZ4Codec.Decode(raw, 4, block.Size - 4, decoded, 0, expectedSize);
					}
				}

				if (decoded.Length < expectedSize) Array.Resize(ref decoded, expectedSize);
				pixelData.InsertRange(0, decoded);
			}

			var final = new List<byte>();
			final.AddRange(ddsHeader);
			if (ddsHeaderDx10 != null) final.AddRange(ddsHeaderDx10);
			final.AddRange(pixelData);
			return final.ToArray();
		}

		private static void SaveAsImage(byte[] ddsData, string outputPath, ImageFormat format)
		{
			using var ms = new MemoryStream(ddsData);
			using var image = Pfim.Pfimage.FromStream(ms);

			System.Drawing.Imaging.PixelFormat pf = image.Format switch
			{
				Pfim.ImageFormat.Rgba32 => System.Drawing.Imaging.PixelFormat.Format32bppArgb,
				Pfim.ImageFormat.Rgb24 => System.Drawing.Imaging.PixelFormat.Format24bppRgb,
				Pfim.ImageFormat.Rgb8 => System.Drawing.Imaging.PixelFormat.Format8bppIndexed,
				_ => throw new NotImplementedException($"Format {image.Format} is not supported.")
			};

			var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
			try
			{
				IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
				using var bitmap = new Bitmap(image.Width, image.Height, image.Stride, pf, ptr);
				bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
				bitmap.Save(outputPath, format);
			}
			finally
			{
				handle.Free();
			}
		}
	}
}
