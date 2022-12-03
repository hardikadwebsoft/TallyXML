using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Nop.Plugin.Widgets.TallyXML.Components
{
    [ViewComponent(Name = "TallyXMLPlugin")]
    public class WidgetsTallyXMLComponent
    {
        public async Task<string> InvokeAsync()
        {
            return "";
        }
    }
}
