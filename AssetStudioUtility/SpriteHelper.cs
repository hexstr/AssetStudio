﻿using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using SharpDX;
using RectangleF = System.Drawing.RectangleF;

namespace AssetStudio
{
    public static class SpriteHelper
    {
        public static Bitmap GetImageFromSprite(Sprite m_Sprite)
        {
            if (m_Sprite.m_SpriteAtlas != null && m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlas))
            {
                if (m_SpriteAtlas.m_RenderDataMap.TryGetValue(m_Sprite.m_RenderDataKey, out var spriteAtlasData) && spriteAtlasData.texture.TryGet(out var m_Texture2D))
                {
                    return CutImage(m_Texture2D, m_Sprite, spriteAtlasData.textureRect, spriteAtlasData.settingsRaw);
                }
            }
            else
            {
                if (m_Sprite.m_RD.texture.TryGet(out var m_Texture2D))
                {
                    return CutImage(m_Texture2D, m_Sprite, m_Sprite.m_RD.textureRect, m_Sprite.m_RD.settingsRaw);
                }
            }
            return null;
        }

        private static Bitmap CutImage(Texture2D m_Texture2D, Sprite m_Sprite, RectangleF textureRect, SpriteSettings settingsRaw)
        {
            var texture2D = new Texture2DConverter(m_Texture2D);
            var originalImage = texture2D.ConvertToBitmap(false);
            if (originalImage != null)
            {
                using (originalImage)
                {
                    var spriteImage = originalImage.Clone(textureRect, PixelFormat.Format32bppArgb);

                    if (settingsRaw.packed == 1)
                    {
                        //RotateAndFlip
                        switch (settingsRaw.packingRotation)
                        {
                            case SpritePackingRotation.kSPRFlipHorizontal:
                                spriteImage.RotateFlip(RotateFlipType.RotateNoneFlipX);
                                break;
                            case SpritePackingRotation.kSPRFlipVertical:
                                spriteImage.RotateFlip(RotateFlipType.RotateNoneFlipY);
                                break;
                            case SpritePackingRotation.kSPRRotate180:
                                spriteImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
                                break;
                            case SpritePackingRotation.kSPRRotate90:
                                spriteImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                                break;
                        }

                        //Tight
                        //TODO 2017 and up use m_PhysicsShape should be better
                        if (settingsRaw.packingMode == SpritePackingMode.kSPMTight)
                        {
                            try
                            {
                                var triangles = GetTriangles(m_Sprite.m_RD);
                                var points = triangles.Select(x => x.Select(y => new PointF(y.X, y.Y)).ToArray());
                                using (var path = new GraphicsPath())
                                {
                                    foreach (var p in points)
                                    {
                                        path.AddPolygon(p);
                                    }
                                    using (var matr = new System.Drawing.Drawing2D.Matrix())
                                    {
                                        if (m_Sprite.m_Pivot == Vector2.Zero) //5.4.2 down
                                        {
                                            matr.Translate(m_Sprite.m_Rect.Width * 0.5f, m_Sprite.m_Rect.Height * 0.5f);
                                        }
                                        else
                                        {
                                            matr.Translate(m_Sprite.m_Rect.Width * m_Sprite.m_Pivot.X, m_Sprite.m_Rect.Height * m_Sprite.m_Pivot.Y);
                                        }
                                        matr.Scale(m_Sprite.m_PixelsToUnits, m_Sprite.m_PixelsToUnits);
                                        path.Transform(matr);
                                        var bitmap = new Bitmap((int)textureRect.Width, (int)textureRect.Height);
                                        using (var graphic = Graphics.FromImage(bitmap))
                                        {
                                            using (var brush = new TextureBrush(spriteImage))
                                            {
                                                graphic.FillPath(brush, path);
                                                bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                                                return bitmap;
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }

                    //Rectangle
                    spriteImage.RotateFlip(RotateFlipType.RotateNoneFlipY);
                    return spriteImage;
                }
            }

            return null;
        }

        private static Vector2[][] GetTriangles(SpriteRenderData m_RD)
        {
            if (m_RD.vertices != null) //5.6 down
            {
                var vertices = m_RD.vertices.Select(x => (Vector2)x.pos).ToArray();
                var triangleCount = m_RD.indices.Length / 3;
                var triangles = new Vector2[triangleCount][];
                for (int i = 0; i < triangleCount; i++)
                {
                    var first = m_RD.indices[i * 3];
                    var second = m_RD.indices[i * 3 + 1];
                    var third = m_RD.indices[i * 3 + 2];
                    var triangle = new[] { vertices[first], vertices[second], vertices[third] };
                    triangles[i] = triangle;
                }
                return triangles;
            }

            return GetTriangles(m_RD.m_VertexData, m_RD.m_SubMeshes, m_RD.m_IndexBuffer); //5.6 and up
        }

        private static Vector2[][] GetTriangles(VertexData m_VertexData, SubMesh[] m_SubMeshes, byte[] m_IndexBuffer)
        {
            var triangles = new List<Vector2[]>();
            GetStreams(m_VertexData);
            var m_Channel = m_VertexData.m_Channels[0]; //kShaderChannelVertex
            var m_Stream = m_VertexData.m_Streams[m_Channel.stream];
            using (BinaryReader vertexReader = new BinaryReader(new MemoryStream(m_VertexData.m_DataSize)),
                                indexReader = new BinaryReader(new MemoryStream(m_IndexBuffer)))
            {
                foreach (var subMesh in m_SubMeshes)
                {
                    vertexReader.BaseStream.Position = m_Stream.offset + subMesh.firstVertex * m_Stream.stride + m_Channel.offset;

                    var vertices = new Vector2[subMesh.vertexCount];
                    for (int v = 0; v < subMesh.vertexCount; v++)
                    {
                        vertices[v] = (Vector2)vertexReader.ReadVector3();
                        vertexReader.BaseStream.Position += m_Stream.stride - 12;
                    }

                    indexReader.BaseStream.Position = subMesh.firstByte;

                    var triangleCount = subMesh.indexCount / 3u;
                    for (int i = 0; i < triangleCount; i++)
                    {
                        var first = indexReader.ReadUInt16() - subMesh.firstVertex;
                        var second = indexReader.ReadUInt16() - subMesh.firstVertex;
                        var third = indexReader.ReadUInt16() - subMesh.firstVertex;
                        var triangle = new[] { vertices[first], vertices[second], vertices[third] };
                        triangles.Add(triangle);
                    }
                }
            }
            return triangles.ToArray();
        }

        private static void GetStreams(VertexData vertexData)
        {
            var m_Channels = vertexData.m_Channels;
            var streamCount = m_Channels.Max(x => x.stream) + 1;
            var m_Streams = new StreamInfo[streamCount];
            uint offset = 0;
            for (int s = 0; s < streamCount; s++)
            {
                uint chnMask = 0;
                uint stride = 0;
                for (int chn = 0; chn < m_Channels.Length; chn++)
                {
                    var m_Channel = m_Channels[chn];
                    if (m_Channel.stream == s)
                    {
                        if (m_Channel.dimension > 0)
                        {
                            chnMask |= 1u << chn;
                            stride += m_Channel.dimension * GetChannelFormatSize(m_Channel.format);
                        }
                    }
                }
                m_Streams[s] = new StreamInfo
                {
                    channelMask = chnMask,
                    offset = offset,
                    stride = stride,
                    dividerOp = 0,
                    frequency = 0
                };
                offset += vertexData.m_VertexCount * stride;
                //static size_t AlignStreamSize (size_t size) { return (size + (kVertexStreamAlign-1)) & ~(kVertexStreamAlign-1); }
                offset = (offset + (16u - 1u)) & ~(16u - 1u);
            }

            vertexData.m_Streams = m_Streams;
        }

        private static uint GetChannelFormatSize(int format)
        {
            switch (format)
            {
                case 0: //kChannelFormatFloat
                    return 4u;
                case 1: //kChannelFormatFloat16
                    return 2u;
                case 2: //kChannelFormatColor, in 4.x is size 4
                    return 1u;
                case 3: //kChannelFormatByte
                    return 1u;
                case 11: //kChannelFormatInt32
                    return 4u;
                default:
                    return 0;
            }
        }
    }
}
