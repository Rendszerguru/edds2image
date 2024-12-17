using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using K4os.Compression.LZ4.Encoders;
using Pfim;

namespace edds2image
{
	internal class Program
	{
		static void Main(string[] args)
		{
			string directoryPath = Directory.GetCurrentDirectory();
			string[] eddsFiles = Directory.GetFiles(directoryPath, "*.edds");

			if (eddsFiles.Length == 0)
			{
				Console.WriteLine("No .edds file found.");
				return;
			}

			string pngFolderPath = Path.Combine(directoryPath, "png");
			string tifFolderPath = Path.Combine(directoryPath, "tif");
			string ddsFolderPath = Path.Combine(directoryPath, "dds");
			Directory.CreateDirectory(pngFolderPath);
			Directory.CreateDirectory(tifFolderPath);
			Directory.CreateDirectory(ddsFolderPath);

			foreach (var eddsPath in eddsFiles)
			{
				string ddsPath = Path.Combine(ddsFolderPath, Path.GetFileNameWithoutExtension(eddsPath) + ".dds");
				string pngPath = Path.Combine(pngFolderPath, Path.GetFileNameWithoutExtension(eddsPath) + ".png");
				string tifPath = Path.Combine(tifFolderPath, Path.GetFileNameWithoutExtension(eddsPath) + ".tif");

				ConvertToDDS(eddsPath, ddsPath);
				Console.WriteLine($"{eddsPath} -> {ddsPath}");

				ConvertEDDSToImage(eddsPath, pngPath, System.Drawing.Imaging.ImageFormat.Png);
				Console.WriteLine($"{eddsPath} -> {pngPath}");

				ConvertEDDSToImage(eddsPath, tifPath, System.Drawing.Imaging.ImageFormat.Tiff);
				Console.WriteLine($"{eddsPath} -> {tifPath}");
			}
		}

		private static void ConvertToDDS(string eddsPath, string ddsPath)
		{
			List<int> copyBlocks = new List<int>();
			List<int> lz4Blocks = new List<int>();
			List<byte> decodedBlocks = new List<byte>();

			void FindBlocks(BinaryReader reader)
			{
				while (true)
				{
					byte[] blocks = reader.ReadBytes(4);
					string block = Encoding.UTF8.GetString(blocks);
					int size = reader.ReadInt32();

					switch (block)
					{
						case "COPY": copyBlocks.Add(size); break;
						case "LZ4 ": lz4Blocks.Add(size); break;
						default: reader.BaseStream.Seek(-8, SeekOrigin.Current); return;
					}
				}
			}

			using (var reader = new BinaryReader(File.Open(eddsPath, FileMode.Open)))
			{
				byte[] ddsHeader = reader.ReadBytes(128);
				byte[] ddsHeaderDx10 = null;

				if (ddsHeader[84] == 'D' && ddsHeader[85] == 'X' && ddsHeader[86] == '1' && ddsHeader[87] == '0')
				{
					ddsHeaderDx10 = reader.ReadBytes(20);
				}

				FindBlocks(reader);

				foreach (int count in copyBlocks)
				{
					byte[] buff = reader.ReadBytes(count);
					decodedBlocks.InsertRange(0, buff);
				}

				foreach (int length in lz4Blocks)
				{
					LZ4ChainDecoder lz4ChainDecoder = new LZ4ChainDecoder(65536, 0);
					uint size = reader.ReadUInt32();
					byte[] target = new byte[size];

					int num = 0;
					int count1 = 0;
					int idx = 0;
					for (; num < length - 4; num += count1 + 4)
					{
						count1 = reader.ReadInt32() & int.MaxValue;
						byte[] numArray = reader.ReadBytes(count1);
						byte[] buffer = new byte[65536];
						LZ4EncoderExtensions.DecodeAndDrain(lz4ChainDecoder, numArray, 0, count1, buffer, 0, 65536, out int count2);

						Array.Copy(buffer, 0, target, idx, count2);
						idx += count2;
					}

					decodedBlocks.InsertRange(0, target);
				}

				if (ddsHeaderDx10 != null)
				{
					decodedBlocks.InsertRange(0, ddsHeaderDx10);
				}

				decodedBlocks.InsertRange(0, ddsHeader);
				byte[] final = decodedBlocks.ToArray();

				using (var wr = File.Create(ddsPath))
				{
					wr.Write(final, 0, final.Length);
				}
			}
		}

		private static void ConvertEDDSToImage(string eddsPath, string outputPath, System.Drawing.Imaging.ImageFormat imageFormat)
		{
			using (var stream = DecompressDDS(eddsPath))
			{
				using (var image = Pfim.Pfimage.FromStream(stream))
				{
					PixelFormat format;
					switch (image.Format)
					{
						case Pfim.ImageFormat.Rgba32:
							format = PixelFormat.Format32bppArgb;
							break;
						case Pfim.ImageFormat.Rgb24:
							format = PixelFormat.Format24bppRgb;
							break;
						case Pfim.ImageFormat.Rgb8:
							format = PixelFormat.Format8bppIndexed;
							break;
						default:
							throw new NotImplementedException($"Unsupported image format: {image.Format}");
					}

					var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
					try
					{
						var data = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
						var bitmap = new Bitmap(image.Width, image.Height, image.Stride, format, data);
						bitmap.Save(outputPath, imageFormat);
					}
					finally
					{
						handle.Free();
					}
				}
			}
		}

		private static MemoryStream DecompressDDS(string ddsPath)
		{
			List<int> copyBlocks = new List<int>();
			List<int> lz4Blocks = new List<int>();
			List<byte> decodedBlocks = new List<byte>();

			void FindBlocks(BinaryReader reader)
			{
				while (true)
				{
					byte[] blocks = reader.ReadBytes(4);
					string block = Encoding.UTF8.GetString(blocks);
					int size = reader.ReadInt32();

					switch (block)
					{
						case "COPY": copyBlocks.Add(size); break;
						case "LZ4 ": lz4Blocks.Add(size); break;
						default: reader.BaseStream.Seek(-8, SeekOrigin.Current); return;
					}
				}
			}

			using (var reader = new BinaryReader(File.Open(ddsPath, FileMode.Open)))
			{
				byte[] ddsHeader = reader.ReadBytes(128);
				byte[] ddsHeaderDx10 = null;

				if (ddsHeader[84] == 'D' && ddsHeader[85] == 'X' && ddsHeader[86] == '1' && ddsHeader[87] == '0')
				{
					ddsHeaderDx10 = reader.ReadBytes(20);
				}

				FindBlocks(reader);

				foreach (int count in copyBlocks)
				{
					byte[] buff = reader.ReadBytes(count);
					decodedBlocks.InsertRange(0, buff);
				}

				foreach (int length in lz4Blocks)
				{
					LZ4ChainDecoder lz4ChainDecoder = new LZ4ChainDecoder(65536, 0);
					uint size = reader.ReadUInt32();
					byte[] target = new byte[size];

					int num = 0;
					int count1 = 0;
					int idx = 0;
					for (; num < length - 4; num += count1 + 4)
					{
						count1 = reader.ReadInt32() & int.MaxValue;
						byte[] numArray = reader.ReadBytes(count1);
						byte[] buffer = new byte[65536];
						LZ4EncoderExtensions.DecodeAndDrain(lz4ChainDecoder, numArray, 0, count1, buffer, 0, 65536, out int count2);

						Array.Copy(buffer, 0, target, idx, count2);
						idx += count2;
					}

					decodedBlocks.InsertRange(0, target);
				}

				if (ddsHeaderDx10 != null)
				{
					decodedBlocks.InsertRange(0, ddsHeaderDx10);
				}

				decodedBlocks.InsertRange(0, ddsHeader);
				byte[] final = decodedBlocks.ToArray();

				return new MemoryStream(final);
			}
		}
	}
}
