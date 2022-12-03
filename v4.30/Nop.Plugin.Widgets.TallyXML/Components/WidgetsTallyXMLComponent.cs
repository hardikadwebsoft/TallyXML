using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Widgets.TallyXML.Components
{
    [ViewComponent(Name = "TexlPlugin")]
    public class WidgetsTallyXMLComponent
    {
        public async Task<string> InvokeAsync()
        {
            return "";
        }
    }
}
