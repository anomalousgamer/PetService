using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace PetService.Core;
internal static class PngIcon
{
 internal static byte[] EncodeBgra(int width,int height,byte[] pixels)
 {
  if(width is <1 or >256||height is <1 or >256||pixels.Length<width*height*4)throw new ArgumentException("Invalid icon dimensions.");
  using var png=new MemoryStream();png.Write(new byte[]{137,80,78,71,13,10,26,10});
  var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk(png,"IHDR",header);
  using var compressed=new MemoryStream();using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true))for(var y=0;y<height;y++){z.WriteByte(0);for(var x=0;x<width;x++){var i=(y*width+x)*4;z.WriteByte(pixels[i+2]);z.WriteByte(pixels[i+1]);z.WriteByte(pixels[i]);z.WriteByte(pixels[i+3]);}}
  Chunk(png,"IDAT",compressed.ToArray());Chunk(png,"IEND",[]);if(png.Length>131072)throw new ArgumentException("Icon too large.");return png.ToArray();
 }
 private static void Chunk(Stream output,string kind,byte[] data)
 {
  var name=Encoding.ASCII.GetBytes(kind);Span<byte> size=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(size,data.Length);output.Write(size);output.Write(name);output.Write(data);
  uint crc=0xffffffff;foreach(var b in name.Concat(data)){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}
  BinaryPrimitives.WriteUInt32BigEndian(size,~crc);output.Write(size);
 }
}
