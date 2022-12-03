using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Models.Orders;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml;
using Microsoft.AspNetCore.Hosting;
using Nop.Services.Catalog;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using System.IO;
using Ionic.Zip;
using Nop.Services.Customers;
using System.Text.RegularExpressions;
using Nop.Web.Areas.Admin.Factories;
using Nop.Core.Domain.Orders;


namespace Nop.Plugin.Widgets.TallyXML.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class WidgetsTallyXMLController : BasePluginController
    {
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IOrderService _orderService;
        private readonly IPermissionService _permissionService;
        private readonly IProductService _productService;
        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private IHostingEnvironment _IHosting;
        private readonly IOrderModelFactory _orderModelFactory;
        public WidgetsTallyXMLController(
                IDateTimeHelper dateTimeHelper,
                ILocalizationService localizationService,
                INotificationService notificationService,
                IOrderService orderService,
                IProductService productService,
                IPermissionService permissionService,
                IWorkContext workContext,
                IHostingEnvironment iHosting,
                IOrderModelFactory orderModelFactory,
                ICustomerService customerService)
        {
            _dateTimeHelper = dateTimeHelper;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _orderService = orderService;
            _permissionService = permissionService;
            _productService = productService;
            _workContext = workContext;
            _IHosting = iHosting;
            _customerService = customerService;
            _orderModelFactory = orderModelFactory;
        }
        protected virtual bool HasAccessToOrder(Order order)
        {
            return order != null && HasAccessToOrder(order.Id);
        }

        protected virtual bool HasAccessToOrder(int orderId)
        {
            if (orderId == 0)
                return false;

            if (_workContext.CurrentVendor == null)
                //not a vendor; has access
                return true;

            var vendorId = _workContext.CurrentVendor.Id;
            var hasVendorProducts = _orderService.GetOrderItems(orderId, vendorId: vendorId).Any();

            return hasVendorProducts;
        }

        [HttpPost]
        public virtual IActionResult OrderList(OrderSearchModel searchModel)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageOrders))
                return AccessDeniedDataTablesJson();

            //prepare model
            var model = _orderModelFactory.PrepareOrderListModel(searchModel);
            var orderData = model.Data.Where(x => x.PaymentStatus == "Paid").ToList();
            model.Data = orderData;
            return Json(model);
        }
        public virtual IActionResult AdminView(List<int> orderStatuses = null, List<int> paymentStatuses = null, List<int> shippingStatuses = null)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageOrders))
                return AccessDeniedView();

            //prepare model
            var model = _orderModelFactory.PrepareOrderSearchModel(new OrderSearchModel
            {
                OrderStatusIds = orderStatuses,
                PaymentStatusIds = paymentStatuses,
                ShippingStatusIds = shippingStatuses
            });

            return View("~/Plugins/Widgets.TallyXML/Views/AdminView.cshtml", model);
        }

        [HttpPost, ActionName("ExportXml")]
        [FormValueRequired("exportxml-all")]
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual IActionResult ExportXmlAll(OrderSearchModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageOrders))
                return AccessDeniedView();

            var startDateValue = model.StartDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            var endDateValue = model.EndDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.EndDate.Value, _dateTimeHelper.CurrentTimeZone).AddDays(1);

            //a vendor should have access only to his products
            if (_workContext.CurrentVendor != null)
            {
                model.VendorId = _workContext.CurrentVendor.Id;
            }

            var orderStatusIds = model.OrderStatusIds != null && !model.OrderStatusIds.Contains(0)
                ? model.OrderStatusIds.ToList()
                : null;
            var paymentStatusIds = model.PaymentStatusIds != null && !model.PaymentStatusIds.Contains(0)
                ? model.PaymentStatusIds.ToList()
                : null;
            var shippingStatusIds = model.ShippingStatusIds != null && !model.ShippingStatusIds.Contains(0)
                ? model.ShippingStatusIds.ToList()
                : null;

            var filterByProductId = 0;
            var product = _productService.GetProductById(model.ProductId);
            if (product != null && (_workContext.CurrentVendor == null || product.VendorId == _workContext.CurrentVendor.Id))
                filterByProductId = model.ProductId;

            //load orders
            var orders = _orderService.SearchOrders(storeId: model.StoreId,
               vendorId: model.VendorId,
               productId: filterByProductId,
               warehouseId: model.WarehouseId,
               paymentMethodSystemName: model.PaymentMethodSystemName,
               createdFromUtc: startDateValue,
               createdToUtc: endDateValue,
               osIds: orderStatusIds,
               psIds: paymentStatusIds,
               ssIds: shippingStatusIds,
               billingPhone: model.BillingPhone,
               billingEmail: model.BillingEmail,
               billingLastName: model.BillingLastName,
               billingCountryId: model.BillingCountryId,
               orderNotes: model.OrderNotes);

            var paidOrders = orders.Where(x => x.PaymentStatus == Core.Domain.Payments.PaymentStatus.Paid).ToList();

            //ensure that we at least one order selected
            if (!orders.Any())
            {
                _notificationService.ErrorNotification(_localizationService.GetResource("Admin.Orders.NoOrders"));
                return RedirectToAction("List");
            }
            try
            {
                DownloadXML(paidOrders);
                using (ZipFile zip = new ZipFile())
                {
                    zip.AlternateEncodingUsage = ZipOption.AsNecessary;
                    zip.AddDirectoryByName("Files");
                    zip.AddFile(_IHosting.WebRootPath + "/files/Master.xml", "files");
                    zip.AddFile(_IHosting.WebRootPath + "/files/Voucher.xml", "files");
                    string zipName = String.Format("Zip_{0}.zip", DateTime.Now.ToString("yyyy-MMM-dd-HHmmss"));
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        zip.Save(memoryStream);
                        return File(memoryStream.ToArray(), "application/zip", zipName);
                    }
                }
            }
            catch (Exception exc)
            {
                _notificationService.ErrorNotification(exc);
                return RedirectToAction("List");
            }
        }

        [HttpPost]
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual IActionResult ExportXmlSelected(string selectedIds)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageOrders))
                return AccessDeniedView();

            var orders = new List<Order>();
            if (selectedIds != null)
            {
                var ids = selectedIds
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Convert.ToInt32(x))
                    .ToArray();
                orders.AddRange(_orderService.GetOrdersByIds(ids).Where(HasAccessToOrder));
            }
            try
            {
                DownloadXML(orders);
                using (ZipFile zip = new ZipFile())
                {
                    zip.AlternateEncodingUsage = ZipOption.AsNecessary;
                    zip.AddDirectoryByName("Files");
                    zip.AddFile(_IHosting.WebRootPath + "/files/Master.xml", "files");
                    zip.AddFile(_IHosting.WebRootPath + "/files/Voucher.xml", "files");
                    string zipName = String.Format("Zip_{0}.zip", DateTime.Now.ToString("yyyy-MMM-dd-HHmmss"));
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        zip.Save(memoryStream);
                        return File(memoryStream.ToArray(), "application/zip", zipName);
                    }
                }
            }
            catch (Exception exc)
            {
                _notificationService.ErrorNotification(exc);
                return RedirectToAction("List");
            }
        }

        public async void DownloadXML(IList<Order> orders)
        {
            try
            {
                #region STEP-1 : Convert_Excel_Content_Into_Tally_Specified_Master's_Format

                StringBuilder sb = new StringBuilder();
                XmlWriterSettings xws = new XmlWriterSettings();
                xws.OmitXmlDeclaration = true;
                xws.Indent = true;
                xws.CheckCharacters = true;
                using (XmlWriter xw = XmlWriter.Create(sb, xws))
                {
                    XElement rootElement = new XElement("ENVELOPE",
                                             new XElement("HEADER",
                                                new XElement("TALLYREQUEST", "Import Data"))
                                               );
                    XElement bodyElement = new XElement("BODY");
                    XElement importData = new XElement(new XElement("IMPORTDATA",
                                                        new XElement("REQUESTDESC",
                                                            new XElement("REPORTNAME", "AllMasters"),
                                                            new XElement("STATICVARIABLES",
                                                                new XElement("SVCURRENTCOMPANY", "##SVCURRENTCOMPANY")))
                                                   ));
                    XElement requestData = new XElement("REQUESTDATA");

                    foreach (var m in orders)
                    {
                        var customer = _customerService.GetCustomerById(m.CustomerId);
                        requestData.Add(new XElement("TALLYMESSAGE", new XAttribute(XNamespace.Xmlns + "UDF", "TallyUDF"),
                                           new XElement("LEDGER", new XAttribute("ACTION", "Create"), new XAttribute("Name", customer.Username),
                                               new XElement("NAME.LIST",
                                                 new XElement("NAME", customer.Username)),
                                               new XElement("CURRENCYNAME", "INR"),
                                               new XElement("GSTREGISTRATIONTYPE", "Regular"),
                                               new XElement("PARENT", "Sundry Debtors"),
                                               new XElement("ISINTERESTON", "No"),
                                               new XElement("ALLOWINMOBILE", "No"),
                                               new XElement("ISCONDENSED", "No"),
                                               new XElement("FORPAYROLL", "No"),
                                               new XElement("INTERESTONBILLWISE", "INR"),
                                               new XElement("OVERRIDEINTEREST", "No"),
                                               new XElement("OVERRIDEADVINTEREST", "No"),
                                               new XElement("USEFORVAT", "No"),
                                               new XElement("IGNORETDSEXEMPT", "No"),
                                               new XElement("ISTCSAPPLICABLE", "No"),
                                               new XElement("ISTDSAPPLICABLE", "No"),
                                               new XElement("ISFBTAPPLICABLE", "No"),
                                               new XElement("ISGSTAPPLICABLE", "No"),
                                               new XElement("SHOWINPAYSLIP", "No"),
                                               new XElement("USEFORGRATUITY", "No"),
                                               new XElement("FORSERVICETAX", "No"),
                                               new XElement("ISINPUTCREDIT", "No"),
                                               new XElement("ISSUBLEDGER", "YES"),
                                               new XElement("ISBILLWISEON", "No"),
                                               new XElement("ISCOSTCENTRESON", "No"),
                                               new XElement("ISEXEMPTED", "No"),
                                               new XElement("TDSDEDUCTEEISSPECIALRATE", "No"),
                                               new XElement("AUDITED", "INR"),
                                               new XElement("SORTPOSITION", "1000")
                                               )));
                    }
                    importData.Add(requestData);
                    bodyElement.Add(importData);
                    rootElement.Add(bodyElement);
                    rootElement.WriteTo(xw);
                }
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(sb.ToString());
                doc.Save(_IHosting.WebRootPath + "/files/Master.xml");

                #endregion

                #region STEP-2 : Convert_Excel_Content_Into_Tally_Specified_Voucher's_Format

                StringBuilder sb1 = new StringBuilder();
                XmlWriterSettings xws1 = new XmlWriterSettings();
                xws.OmitXmlDeclaration = true;
                xws.Indent = true;
                xws.CheckCharacters = true;
                using (XmlWriter xw1 = XmlWriter.Create(sb1, xws1))
                {
                    XElement rootElement = new XElement("ENVELOPE",
                                             new XElement("HEADER",
                                                new XElement("TALLYREQUEST", "Import Data"))
                                           );
                    XElement bodyElement = new XElement("BODY");
                    XElement importData = new XElement(new XElement("IMPORTDATA",
                                                        new XElement("REQUESTDESC",
                                                            new XElement("REPORTNAME", "AllMasters"),
                                                            new XElement("STATICVARIABLES",
                                                                new XElement("SVCURRENTCOMPANY", "##SVCURRENTCOMPANY")))
                                                   ));
                    XElement requestData = new XElement("REQUESTDATA");

                    foreach (var v in orders)
                    {
                        var date = Regex.Replace(v.CreatedOnUtc.ToString("yyyy-MM-dd").Replace('-', ' '), @"\s", "");
                        var customer = _customerService.GetCustomerById(v.CustomerId);
                        requestData.Add(new XElement("TALLYMESSAGE", new XAttribute(XNamespace.Xmlns + "UDF", "TallyUDF"),
                                            new XElement("VOUCHER", new XAttribute("REMOTEID", string.Empty), new XAttribute("VCHTYPE", "Sales"), new XAttribute("ACTION", "Create"),
                                                                        new XElement("DATE", date),
                                                                        new XElement("GUID", string.Empty),
                                                                        new XElement("VOUCHERTYPENAME", "Sales"),
                                                                        new XElement("VOUCHERNUMBER", v.Id),
                                                                        new XElement("PARTYLEDGERNAME", customer.Username),
                                                                        new XElement("CSTFORMISSUETYPE", string.Empty),
                                                                        new XElement("CSTFORMRECVTYPE", string.Empty),
                                                                        new XElement("FBTPAYMENTTYPE", "Default"),
                                                                        new XElement("DIFFACTUALQTY", "No"),
                                                                        new XElement("AUDITED", "No"),
                                                                        new XElement("FORJOBCOSTING", "No"),
                                                                        new XElement("ISOPTIONAL", "No"),
                                                                        new XElement("EFFECTIVEDATE", date),
                                                                        new XElement("USEFORINTEREST", "No"),
                                                                        new XElement("USEFORGAINLOSS", "No"),
                                                                        new XElement("USEFORGODOWNTRANSFER", "No"),
                                                                        new XElement("USEFORCOMPOUND", "No"),
                                                                        new XElement("ALTERID", string.Empty),
                                                                        new XElement("EXCISEOPENING", "No"),
                                                                        new XElement("USEFORFINALPRODUCTION", "No"),
                                                                        new XElement("ISCANCELLED", "No"),
                                                                        new XElement("HASCASHFLOW", "No"),
                                                                        new XElement("ISPOSTDATED", "No"),
                                                                        new XElement("USETRACKINGNUMBER", "No"),
                                                                        new XElement("ISINVOICE", "No"),
                                                                        new XElement("MFGJOURNAL", "No"),
                                                                        new XElement("HASDISCOUNTS", "No"),
                                                                        new XElement("ASPAYSLIP", "No"),
                                                                        new XElement("ISCOSTCENTRE", "No"),
                                                                        new XElement("ISDELETED", "No"),
                                                                        new XElement("ASORIGINAL", "No"),
                                                                        new XElement("ALLLEDGERENTRIES.LIST",
                                                                         new XElement("LEDGERNAME", customer.Username),
                                                                         new XElement("GSTCLASS", string.Empty),
                                                                         new XElement("ISDEEMEDPOSITIVE", "Yes"),
                                                                         new XElement("LEDGERFROMITEM", "No"),
                                                                         new XElement("REMOVEZEROENTRIES", "No"),
                                                                         new XElement("ISPARTYLEDGER", "Yes"),
                                                                         new XElement("AMOUNT", "-" + v.OrderTotal)),
                                                                        new XElement("ALLLEDGERENTRIES.LIST",
                                                                         new XElement("LEDGERNAME", "Cash"),
                                                                         new XElement("GSTCLASS", string.Empty),
                                                                         new XElement("ISDEEMEDPOSITIVE", "No"),
                                                                         new XElement("LEDGERFROMITEM", "No"),
                                                                         new XElement("REMOVEZEROENTRIES", "No"),
                                                                         new XElement("ISPARTYLEDGER", "No"),
                                                                         new XElement("AMOUNT", v.OrderTotal))
                                                )));
                    }
                    importData.Add(requestData);
                    bodyElement.Add(importData);
                    rootElement.Add(bodyElement);
                    rootElement.WriteTo(xw1);
                }
                XmlDocument doc1 = new XmlDocument();
                doc1.LoadXml(sb1.ToString());
                doc1.Save(_IHosting.WebRootPath + "/files/Voucher.xml");


                string[] filePaths = Directory.GetFiles(_IHosting.WebRootPath + "/files");
                var files = new List<string>();

                files.Add(_IHosting.WebRootPath + "/files/Voucher.xml");
                files.Add(_IHosting.WebRootPath + "/files/Master.xml");

                #endregion
            }
            catch (Exception exc)
            {
                _notificationService.ErrorNotification(exc);
            }
        }
    }
}