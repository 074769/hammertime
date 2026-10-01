using System;
using System.Drawing;

namespace Sledge.Common.Shell.Menu
{
    /// <summary>
    /// An attribute that can be attached to a class to indicate the class' menu item.
    /// Uses an embedded SVG with the same name as the resource if there is one, otherwise the PNG resource.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class MenuImageAttribute : Attribute
    {
        public Type ResourceType { get; set; }
        public string ResourceName { get; set; }
        public Image Image => IconLoader.Load(ResourceType, ResourceName);

        /// <summary>The image rendered at the given pixel size (crisp if there is an SVG, otherwise the PNG at its own size).</summary>
        public Image GetImage(int size) => IconLoader.Load(ResourceType, ResourceName, size);

        public MenuImageAttribute(Type resourceType, string resourceName)
        {
            ResourceType = resourceType;
            ResourceName = resourceName;
        }
    }
}
