using System.Collections.Generic;
using System.Drawing;
using Sledge.Common;
using Sledge.Providers.Texture.Spr;

namespace Sledge.Providers.Texture
{
    public class TextureItem
    {
        public string Name { get; }
        public TextureFlags Flags { get; }
        public string Reference { get; set; }

        /// <summary>
        /// The orientation baked into the .spr file itself (Parallel, ParallelUpright,
        /// Oriented, ParallelOriented, FacingUpright). Null for non-sprite textures.
        /// </summary>
        public SpriteOrientation? Orientation { get; set; }

        private Dictionary<TextureSubItemType, TextureSubItem> SubItems { get; }

        public TextureSubItem PrimarySubItem => SubItems.ContainsKey(TextureSubItemType.Base) ? SubItems[TextureSubItemType.Base] : null;
        public IEnumerable<TextureSubItem> AllSubItems => SubItems.Values;
        
        public int Width => PrimarySubItem.Width;
        public int Height => PrimarySubItem.Height;
        public Size Size => new Size(Width, Height);
        public string WadName { get; set; }

		public TextureItem(string name, TextureFlags flags, int width, int height, string wadName)
        {
            Name = name;
            Flags = flags;
			var baseItem = new TextureSubItem(TextureSubItemType.Base, this, name, width, height);
			SubItems = new Dictionary<TextureSubItemType, TextureSubItem> { { TextureSubItemType.Base, baseItem } };
            WadName = wadName;
		}


		public TextureItem(string name, TextureFlags flags, int width, int height)
        {
            Name = name;
            Flags = flags;
            var baseItem = new TextureSubItem(TextureSubItemType.Base, this, name, width, height);
            SubItems = new Dictionary<TextureSubItemType, TextureSubItem> {{TextureSubItemType.Base, baseItem}};
        }

        public TextureItem(string name, TextureFlags flags, string primarySubItemName, int width, int height)
        {
            Name = name;
            Flags = flags;
            var baseItem = new TextureSubItem(TextureSubItemType.Base, this, primarySubItemName, width, height);
            SubItems = new Dictionary<TextureSubItemType, TextureSubItem> {{TextureSubItemType.Base, baseItem}};
        }

        public TextureItem(string name, TextureFlags flags)
        {
            Name = name;
            Flags = flags;
            SubItems = new Dictionary<TextureSubItemType, TextureSubItem>();
        }

        public TextureSubItem AddSubItem(TextureSubItemType type, string name, int width, int height)
        {
            var si = new TextureSubItem(type, this, name, width, height);
            SubItems.Add(type, si);
            return si;
        }
    }
}
