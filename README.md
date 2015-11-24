**Unity Studio** is a tool for exploring, extracting and exporting assets from Unity games and apps.

It is the continuation of my Unity Importer script for 3ds Max, and comprises all my research and reverse engineering of Unity file formats. It has been thoroughly tested with Unity builds from most platforms, ranging from Web, PC, Linux, MacOS to Xbox360, PS3, Android and iOS, and it is currently maintained to be compatible with Unity builds from 2.5.0 up to the latest version.

#### Current features

* Export to FBX, with complete hierarchy, transformations, materials and textures. At the moment, geometry is exported with normals UV coordinates and vertex colors, but no animation data.
* Extraction of assets that can be used as standalone resources:
  * Textures: DDS (Alpha8bpp, ARGB16bpp, RGB24bpp, ARGB32bpp, BGRA32bpp, RGB565, DXT1, DXT5, RGBA16bpp)
  * PVR (PVRTC_RGB2, PVRTC_RGBA2, PVRTC_RGBA4, PVRTC_RGB4, ETC_RGB4)
  * Audio clips: mp3, ogg, wav, xbox wav (including streams from .resS files)
  * Fonts: ttf, otf
  * Text Assets
  * Shaders
* Real-time preview window for the above-mentioned assets
* Diagnostics mode with useful tools for research


#### UI guide

| Item                          | Action
| :---------------------------- | :----------------------------
| File -> Load file/folder      | Open Assetfiles and load their assets. Load file can also decompress and load bundle files straight into memory
| File -> Extract bundle/folder | Extract Assetfiles from bundle files compressed with lzma or l4z
| Scene Hierarchy search box    | Search nodes using * and ? wildcards. Press Enter to loop through results or Ctrl+Enter to select all matching nodes
| Asset List filter box         | Enter a keyword to filter the list of available assets; wildcards are added automatically
| Diagnostics                   | press Ctrl+Alt+D to bring up a hidden menu and a new list
| Bulid class structures        | Create human-readable structures for each type of Unity asset; available only in Web builds!

Other interface elements have tooltips or are self-explanatory.
