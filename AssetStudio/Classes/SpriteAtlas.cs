﻿using SharpDX;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public class SpriteAtlasData
    {
        public PPtr texture;
        public PPtr alphaTexture;
        public System.Drawing.RectangleF textureRect;
        public Vector2 textureRectOffset;
        public Vector2 atlasRectOffset;
        public Vector4 uvTransform;
        public float downscaleMultiplier;
        public SpriteSettings settingsRaw;

        public SpriteAtlasData(ObjectReader reader)
        {
            var version = reader.version;
            texture = reader.ReadPPtr();
            alphaTexture = reader.ReadPPtr();
            textureRect = reader.ReadRectangleF();
            textureRectOffset = reader.ReadVector2();
            if (version[0] > 2017 || (version[0] == 2017 && version[1] >= 2)) //2017.2 and up
            {
                atlasRectOffset = reader.ReadVector2();
            }
            uvTransform = reader.ReadVector4();
            downscaleMultiplier = reader.ReadSingle();
            settingsRaw = new SpriteSettings(reader);
        }
    }

    public sealed class SpriteAtlas : NamedObject
    {
        public Dictionary<Tuple<Guid, long>, SpriteAtlasData> m_RenderDataMap;

        public SpriteAtlas(ObjectReader reader) : base(reader)
        {
            var m_PackedSpritesSize = reader.ReadInt32();
            for (int i = 0; i < m_PackedSpritesSize; i++)
            {
                reader.ReadPPtr(); //PPtr<Sprite> data
            }

            var m_PackedSpriteNamesToIndexSize = reader.ReadInt32();
            for (int i = 0; i < m_PackedSpriteNamesToIndexSize; i++)
            {
                reader.ReadAlignedString();
            }

            var m_RenderDataMapSize = reader.ReadInt32();
            m_RenderDataMap = new Dictionary<Tuple<Guid, long>, SpriteAtlasData>(m_RenderDataMapSize);
            for (int i = 0; i < m_RenderDataMapSize; i++)
            {
                var first = new Guid(reader.ReadBytes(16));
                var second = reader.ReadInt64();
                var value = new SpriteAtlasData(reader);
                m_RenderDataMap.Add(new Tuple<Guid, long>(first, second), value);
            }
            //string m_Tag
            //bool m_IsVariant
        }
    }
}
