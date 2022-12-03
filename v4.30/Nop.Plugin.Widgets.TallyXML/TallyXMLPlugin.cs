using Microsoft.AspNetCore.Routing;
using Nop.Services.Cms;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Framework.Menu;
using System.Collections.Generic;
using System.Linq;

namespace Nop.Plugin.Widgets.TallyXML
{
    public class TallyXMLPlugin : BasePlugin, IWidgetPlugin, IAdminMenuPlugin
    {
        /// <summary>
        /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
        /// </summary>
        public bool HideInWidgetList => false;

        /// <summary>
        /// Gets a name of a view component for displaying widget
        /// </summary>
        /// <param name="widgetZone">Name of the widget zone</param>
        /// <returns>View component name</returns>
        public string GetWidgetViewComponentName(string widgetZone)
        {
            return "TexlPlugin";
        }
        public void ManageSiteMap(SiteMapNode rootNode)
        {
            var menuItem = new SiteMapNode()
            {
                SystemName = "Widgets.TallyXML",
                Title = "TallyXML",
                ControllerName = "WidgetsTallyXML",
                ActionName = "AdminView",
                IconClass = "fa-file-code-o",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", null } },
            };
            var pluginNode = rootNode.ChildNodes.FirstOrDefault(x => x.SystemName == "My Custom Plugin");
            if (pluginNode != null)
                pluginNode.ChildNodes.Add(menuItem);
            else
                rootNode.ChildNodes.Add(menuItem);

        }
        /// <summary>
        /// Gets widget zones where this widget should be rendered
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the widget zones
        /// </returns>
        public IList<string> GetWidgetZones()
        {
            return (new List<string> { PublicWidgetZones.HomepageBeforeNews });
        }

        public override void Install()
        {
            base.Install();
        }

        public override void Uninstall()
        {
            //Logic during uninstallation goes here...

            base.Uninstall();
        }


    }
}
