using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
	public class AssetInfo
	{
		public int preloadIndex;
		public int preloadSize;
		public PPtr<Object> asset;

		public AssetInfo(ObjectReader reader)
		{
			preloadIndex = reader.ReadInt32();
			preloadSize = reader.ReadInt32();
			asset = new PPtr<Object>(reader);
		}
	}

	public sealed class AssetBundle : NamedObject
	{
		public PPtr<Object>[] m_PreloadTable;
		public KeyValuePair<string, AssetInfo>[] m_Container;
		public string m_AssetBundleName;
		public string[] m_Dependencies;

		public AssetBundle(ObjectReader reader) : base(reader)
		{
			var m_PreloadTableSize = reader.ReadInt32();
			m_PreloadTable = new PPtr<Object>[m_PreloadTableSize];
			for (int i = 0; i < m_PreloadTableSize; i++)
			{
				m_PreloadTable[i] = new PPtr<Object>(reader);
			}

			var m_ContainerSize = reader.ReadInt32();
			m_Container = new KeyValuePair<string, AssetInfo>[m_ContainerSize];
			for (int i = 0; i < m_ContainerSize; i++)
			{
				m_Container[i] = new KeyValuePair<string, AssetInfo>(reader.ReadAlignedString(), new AssetInfo(reader));
			}

			reader.AlignStream();

			if (version[0] == 2017 && version[1] == 4 && version[2] == 18 && version[3] == 1 && version[4] == 2)
			{
				m_AssetBundleName = reader.ReadAlignedString();
				m_Dependencies = reader.ReadStringArray();
				var m_Flags = reader.ReadInt32();
			}
		}
	}
}
