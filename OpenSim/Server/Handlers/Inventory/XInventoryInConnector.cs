/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Data;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using log4net;
using OpenMetaverse;

using System.Threading;

namespace OpenSim.Server.Handlers.Inventory
{
    public class XInventoryInConnector : ServiceConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IInventoryService m_InventoryService;
        private string m_ConfigName = "InventoryService";

        public XInventoryInConnector(IConfigSource config, IHttpServer server, string configName) :
                base(config, server, configName)
        {
            if (configName != String.Empty)
                m_ConfigName = configName;

            m_log.DebugFormat("[XInventoryInConnector]: Starting with config name {0}", m_ConfigName);

            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(String.Format("No section '{0}' in config file", m_ConfigName));

            string inventoryService = serverConfig.GetString("LocalServiceModule",
                    String.Empty);

            if (inventoryService.Length == 0)
                throw new Exception("No InventoryService in config file");

            Object[] args = new Object[] { config, m_ConfigName };
            m_InventoryService =
                    ServerUtils.LoadPlugin<IInventoryService>(inventoryService, args);

            IServiceAuth auth = ServiceAuth.Create(config, m_ConfigName);

            // Only the public HG connector gets a travel/ownership gate.
            HGInventoryTravelGate gate = null;
            if (m_ConfigName.Equals("HGInventoryService", StringComparison.OrdinalIgnoreCase))
                gate = new HGInventoryTravelGate(config);

            server.AddStreamHandler(new XInventoryConnectorPostHandler(m_InventoryService, auth, gate));
        }
    }

    public class XInventoryConnectorPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly ILog m_audit =
            LogManager.GetLogger("HGInventoryAudit");

        private IInventoryService m_InventoryService;

        private readonly HGInventoryTravelGate m_TravelGate;

        public XInventoryConnectorPostHandler(IInventoryService service, IServiceAuth auth)
            : this(service, auth, null) { }

        public XInventoryConnectorPostHandler(IInventoryService service, IServiceAuth auth,
                HGInventoryTravelGate travelGate) : base("POST", "/xinventory", auth)
        {
            m_InventoryService = service;
            m_TravelGate = travelGate;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string body;
            using(StreamReader sr = new StreamReader(requestData))
                body = sr.ReadToEnd();
            body = body.Trim();

            //m_log.DebugFormat("[XXX]: query String: {0}", body);

            try
            {
                Dictionary<string, object> request =
                        ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                    return FailureResult();

                string method = request["METHOD"].ToString();

                m_audit.InfoFormat(
                    "[XINVENTORY AUDIT]: method={0}, principal={1}, owner={2}, id={3}, folder={4}, parent={5}, bytes={6}",
                    method,
                    AuditField(request, "PRINCIPAL"),
                    AuditField(request, "Owner"),
                    AuditField(request, "ID"),
                    AuditField(request, "Folder"),
                    AuditField(request, "ParentID"),
                    Encoding.UTF8.GetByteCount(body));

                if (m_TravelGate != null)
                {
                    string decision;
                    bool allowed = m_TravelGate.Check(method, request, out decision);
                    m_audit.InfoFormat("[HG INVENTORY GATE]: method={0}, decision={1}, reason={2}",
                        AuditField(request, "METHOD"), allowed ? "ALLOW" : "DENY", decision);
                    if (!allowed)
                        return FailureResult();
                }

                request.Remove("METHOD");

                switch (method)
                {
                    case "CREATEUSERINVENTORY":
                        return HandleCreateUserInventory(request);
                    case "GETINVENTORYSKELETON":
                        return HandleGetInventorySkeleton(request);
                    case "GETROOTFOLDER":
                        return HandleGetRootFolder(request);
                    case "GETFOLDERFORTYPE":
                        return HandleGetFolderForType(request);
                    case "GETFOLDERCONTENT":
                        return HandleGetFolderContent(request);
                    case "GETMULTIPLEFOLDERSCONTENT":
                        return HandleGetMultipleFoldersContent(request);
                    case "GETFOLDERITEMS":
                        return HandleGetFolderItems(request);
                    case "ADDFOLDER":
                        return HandleAddFolder(request);
                    case "UPDATEFOLDER":
                        return HandleUpdateFolder(request);
                    case "MOVEFOLDER":
                        return HandleMoveFolder(request);
                    case "DELETEFOLDERS":
                        return HandleDeleteFolders(request);
                    case "PURGEFOLDER":
                        return HandlePurgeFolder(request);
                    case "ADDITEM":
                        return HandleAddItem(request);
                    case "UPDATEITEM":
                        return HandleUpdateItem(request);
                    case "MOVEITEMS":
                        return HandleMoveItems(request);
                    case "DELETEITEMS":
                        return HandleDeleteItems(request);
                    case "GETITEM":
                        return HandleGetItem(request);
                    case "GETMULTIPLEITEMS":
                        return HandleGetMultipleItems(request);
                    case "GETFOLDER":
                        return HandleGetFolder(request);
                    case "GETACTIVEGESTURES":
                        return HandleGetActiveGestures(request);
                    case "GETASSETPERMISSIONS":
                        return HandleGetAssetPermissions(request);
                }
                m_log.DebugFormat("[XINVENTORY HANDLER]: unknown method request: {0}", method);
            }
            catch (Exception e)
            {
                m_log.Error(string.Format("[XINVENTORY HANDLER]: Exception {0} ", e.Message), e);
            }

            return FailureResult();
        }

        private static string AuditField(
            Dictionary<string, object> request, string key)
        {
            if (!request.TryGetValue(key, out object value) || value == null)
                return "-";

            string text = value.ToString()
                .Replace('\r', ' ')
                .Replace('\n', ' ');

            return text.Length > 128 ? text.Substring(0, 128) : text;
        }

        private byte[] FailureResult()
        {
            return BoolResult(false);
        }

        private byte[] SuccessResult()
        {
            return BoolResult(true);
        }

        private byte[] BoolResult(bool value)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration,
                    "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse",
                    "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "RESULT", "");
            result.AppendChild(doc.CreateTextNode(value.ToString()));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
        }

        byte[] HandleCreateUserInventory(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            if (!request.ContainsKey("PRINCIPAL"))
                return FailureResult();

            if (m_InventoryService.CreateUserInventory(new UUID(request["PRINCIPAL"].ToString())))
                result["RESULT"] = "True";
            else
                result["RESULT"] = "False";

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetInventorySkeleton(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            if (!request.ContainsKey("PRINCIPAL"))
                return FailureResult();


            List<InventoryFolderBase> folders = m_InventoryService.GetInventorySkeleton(new UUID(request["PRINCIPAL"].ToString()));

            Dictionary<string, object> sfolders = new Dictionary<string, object>();
            if (folders != null)
            {
                int i = 0;
                foreach (InventoryFolderBase f in folders)
                {
                    sfolders["folder_" + i.ToString()] = EncodeFolder(f);
                    i++;
                }
            }
            result["FOLDERS"] = sfolders;

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetRootFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            InventoryFolderBase rfolder = m_InventoryService.GetRootFolder(principal);
            if (rfolder != null)
                result["folder"] = EncodeFolder(rfolder);

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderForType(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            int type = 0;
            Int32.TryParse(request["TYPE"].ToString(), out type);
            InventoryFolderBase folder = m_InventoryService.GetFolderForType(principal, (FolderType)type);
            if (folder != null)
                result["folder"] = EncodeFolder(folder);

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderContent(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            UUID folderID = UUID.Zero;
            UUID.TryParse(request["FOLDER"].ToString(), out folderID);

            InventoryCollection icoll = m_InventoryService.GetFolderContent(principal, folderID);
            if (icoll != null)
            {
                result["FID"] = icoll.FolderID.ToString();
                result["VERSION"] = icoll.Version.ToString();
                Dictionary<string, object> folders = new Dictionary<string, object>();
                int i = 0;
                if (icoll.Folders != null)
                {
                    foreach (InventoryFolderBase f in icoll.Folders)
                    {
                        folders["folder_" + i.ToString()] = EncodeFolder(f);
                        i++;
                    }
                    result["FOLDERS"] = folders;
                }
                if (icoll.Items != null)
                {
                    i = 0;
                    Dictionary<string, object> items = new Dictionary<string, object>();
                    foreach (InventoryItemBase it in icoll.Items)
                    {
                        items["item_" + i.ToString()] = EncodeItem(it);
                        i++;
                    }
                    result["ITEMS"] = items;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetMultipleFoldersContent(Dictionary<string, object> request)
        {
            Dictionary<string, object> resultSet = new Dictionary<string, object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            string folderIDstr = request["FOLDERS"].ToString();
            int count = 0;
            Int32.TryParse(request["COUNT"].ToString(), out count);

            UUID[] fids = new UUID[count];
            string[] uuids = folderIDstr.Split(',');
            int i = 0;
            foreach (string id in uuids)
            {
                UUID fid = UUID.Zero;
                if (UUID.TryParse(id, out fid))
                    fids[i] = fid;
                i += 1;
            }

            count = 0;
            InventoryCollection[] icollList = m_InventoryService.GetMultipleFoldersContent(principal, fids);
            if (icollList != null && icollList.Length > 0)
            {
                foreach (InventoryCollection icoll in icollList)
                {
                    Dictionary<string, object> result = new Dictionary<string, object>();
                    result["FID"] = icoll.FolderID.ToString();
                    result["VERSION"] = icoll.Version.ToString();
                    result["OWNER"] = icoll.OwnerID.ToString();
                    Dictionary<string, object> folders = new Dictionary<string, object>();
                    i = 0;
                    if (icoll.Folders != null)
                    {
                        foreach (InventoryFolderBase f in icoll.Folders)
                        {
                            folders["folder_" + i.ToString()] = EncodeFolder(f);
                            i++;
                        }
                        result["FOLDERS"] = folders;
                    }
                    i = 0;
                    if (icoll.Items != null)
                    {
                        Dictionary<string, object> items = new Dictionary<string, object>();
                        foreach (InventoryItemBase it in icoll.Items)
                        {
                            items["item_" + i.ToString()] = EncodeItem(it);
                            i++;
                        }
                        result["ITEMS"] = items;
                    }

                    resultSet["F_" + fids[count++]] = result;
                    //m_log.DebugFormat("[XXX]: Sending {0} {1}", fids[count-1], icoll.FolderID);
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(resultSet);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderItems(Dictionary<string, object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            UUID folderID = UUID.Zero;
            UUID.TryParse(request["FOLDER"].ToString(), out folderID);

            List<InventoryItemBase> items = m_InventoryService.GetFolderItems(principal, folderID);
            Dictionary<string, object> sitems = new Dictionary<string, object>();

            if (items != null)
            {
                int i = 0;
                foreach (InventoryItemBase item in items)
                {
                    sitems["item_" + i.ToString()] = EncodeItem(item);
                    i++;
                }
            }
            result["ITEMS"] = sitems;

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleAddFolder(Dictionary<string,object> request)
        {
            InventoryFolderBase folder = BuildFolder(request);

            if (m_InventoryService.AddFolder(folder))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleUpdateFolder(Dictionary<string,object> request)
        {
            InventoryFolderBase folder = BuildFolder(request);

            if (m_InventoryService.UpdateFolder(folder))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleMoveFolder(Dictionary<string,object> request)
        {
            UUID parentID = UUID.Zero;
            UUID.TryParse(request["ParentID"].ToString(), out parentID);
            UUID folderID = UUID.Zero;
            UUID.TryParse(request["ID"].ToString(), out folderID);
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);

            InventoryFolderBase folder = new InventoryFolderBase(folderID, "", principal, parentID);
            if (m_InventoryService.MoveFolder(folder))
                return SuccessResult();
            else
                return FailureResult();

        }

        byte[] HandleDeleteFolders(Dictionary<string,object> request)
        {
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            List<string> slist = (List<string>)request["FOLDERS"];
            List<UUID> uuids = new List<UUID>();
            foreach (string s in slist)
            {
                UUID u = UUID.Zero;
                if (UUID.TryParse(s, out u))
                    uuids.Add(u);
            }

            if (m_InventoryService.DeleteFolders(principal, uuids))
                return SuccessResult();
            else
                return
                    FailureResult();
        }

        byte[] HandlePurgeFolder(Dictionary<string,object> request)
        {
            UUID folderID = UUID.Zero;
            UUID.TryParse(request["ID"].ToString(), out folderID);

            InventoryFolderBase folder = new InventoryFolderBase(folderID);
            if (m_InventoryService.PurgeFolder(folder))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleAddItem(Dictionary<string,object> request)
        {
            InventoryItemBase item = BuildItem(request);

            if (m_InventoryService.AddItem(item))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleUpdateItem(Dictionary<string,object> request)
        {
            InventoryItemBase item = BuildItem(request);

            if (m_InventoryService.UpdateItem(item))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleMoveItems(Dictionary<string,object> request)
        {
            List<string> idlist = (List<string>)request["IDLIST"];
            List<string> destlist = (List<string>)request["DESTLIST"];

            UUID.TryParse(request["PRINCIPAL"].ToString(), out UUID principal);

            List<InventoryItemBase> items = new List<InventoryItemBase>();
            int n = 0;
            try
            {
                foreach (string s in idlist)
                {
                    UUID u = UUID.Zero;
                    if (UUID.TryParse(s, out u))
                    {
                        UUID fid = UUID.Zero;
                        if (UUID.TryParse(destlist[n++], out fid))
                        {
                            InventoryItemBase item = new InventoryItemBase(u, principal);
                            item.Folder = fid;
                            items.Add(item);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[XINVENTORY IN CONNECTOR]: Exception in HandleMoveItems: {0}", e.Message);
                return FailureResult();
            }

            if (m_InventoryService.MoveItems(principal, items))
                return SuccessResult();
            else
                return FailureResult();
        }

        byte[] HandleDeleteItems(Dictionary<string,object> request)
        {
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            List<string> slist = (List<string>)request["ITEMS"];
            List<UUID> uuids = new List<UUID>();
            foreach (string s in slist)
            {
                UUID u = UUID.Zero;
                if (UUID.TryParse(s, out u))
                    uuids.Add(u);
            }

            if (m_InventoryService.DeleteItems(principal, uuids))
                return SuccessResult();
            else
                return
                    FailureResult();
        }

        byte[] HandleGetItem(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID id = UUID.Zero;
            UUID.TryParse(request["ID"].ToString(), out id);
            UUID user = UUID.Zero;
            if (request.ContainsKey("PRINCIPAL"))
                UUID.TryParse(request["PRINCIPAL"].ToString(), out user);

            InventoryItemBase item = m_InventoryService.GetItem(user, id);
            if (item != null)
                result["item"] = EncodeItem(item);

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetMultipleItems(Dictionary<string, object> request)
        {
            Dictionary<string, object> resultSet = new Dictionary<string, object>();

            if(!UUID.TryParse(request["PRINCIPAL"].ToString(), out UUID principal))
                return Util.UTF8NoBomEncoding.GetBytes(ServerUtils.BuildXmlResponse(resultSet));

            string itemIDstr = request["ITEMS"].ToString();
            if(string.IsNullOrEmpty(itemIDstr))
                return Util.UTF8NoBomEncoding.GetBytes(ServerUtils.BuildXmlResponse(resultSet));

            string[] uuids = itemIDstr.Split(',');
            if(uuids.Length == 0)
                return Util.UTF8NoBomEncoding.GetBytes(ServerUtils.BuildXmlResponse(resultSet));

            UUID[] fids = new UUID[uuids.Length];
            int i = 0;
            foreach (string id in uuids)
            {
                UUID fid = UUID.Zero;
                if (UUID.TryParse(id, out fid))
                    fids[i] = fid;
                i += 1;
            }

            InventoryItemBase[] itemsList = m_InventoryService.GetMultipleItems(principal, fids);
            if (itemsList != null && itemsList.Length > 0)
            {
                int count = 0;
                foreach (InventoryItemBase item in itemsList)
                    resultSet["item_" + count++] = (item == null) ? "NULL" : EncodeItem(item);
            }

            string xmlString = ServerUtils.BuildXmlResponse(resultSet);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolder(Dictionary<string,object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            UUID id = UUID.Zero;
            UUID.TryParse(request["ID"].ToString(), out id);
            UUID user = UUID.Zero;
            if (request.ContainsKey("PRINCIPAL"))
                UUID.TryParse(request["PRINCIPAL"].ToString(), out user);

            InventoryFolderBase folder = m_InventoryService.GetFolder(user, id);
            if (folder != null)
                result["folder"] = EncodeFolder(folder);

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetActiveGestures(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);

            List<InventoryItemBase> gestures = m_InventoryService.GetActiveGestures(principal);
            Dictionary<string, object> items = new Dictionary<string, object>();
            if (gestures != null)
            {
                int i = 0;
                foreach (InventoryItemBase item in gestures)
                {
                    items["item_" + i.ToString()] = EncodeItem(item);
                    i++;
                }
            }
            result["ITEMS"] = items;

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGetAssetPermissions(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();
            UUID principal = UUID.Zero;
            UUID.TryParse(request["PRINCIPAL"].ToString(), out principal);
            UUID assetID = UUID.Zero;
            UUID.TryParse(request["ASSET"].ToString(), out assetID);

            int perms = m_InventoryService.GetAssetPermissions(principal, assetID);

            result["RESULT"] = perms.ToString();
            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        private Dictionary<string, object> EncodeFolder(InventoryFolderBase f)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();

            ret["ParentID"] = f.ParentID.ToString();
            ret["Type"] = f.Type.ToString();
            ret["Version"] = f.Version.ToString();
            ret["Name"] = f.Name;
            ret["Owner"] = f.Owner.ToString();
            ret["ID"] = f.ID.ToString();

            return ret;
        }

        private Dictionary<string, object> EncodeItem(InventoryItemBase item)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();

            ret["AssetID"] = item.AssetID.ToString();
            ret["AssetType"] = item.AssetType.ToString();
            ret["BasePermissions"] = item.BasePermissions.ToString();
            ret["CreationDate"] = item.CreationDate.ToString();
            if (item.CreatorId != null)
                ret["CreatorId"] = item.CreatorId.ToString();
            else
                ret["CreatorId"] = String.Empty;
            if (item.CreatorData != null)
                ret["CreatorData"] = item.CreatorData;
            else
                ret["CreatorData"] = String.Empty;
            ret["CurrentPermissions"] = item.CurrentPermissions.ToString();
            ret["Description"] = item.Description.ToString();
            ret["EveryOnePermissions"] = item.EveryOnePermissions.ToString();
            ret["Flags"] = item.Flags.ToString();
            ret["Folder"] = item.Folder.ToString();
            ret["GroupID"] = item.GroupID.ToString();
            ret["GroupOwned"] = item.GroupOwned.ToString();
            ret["GroupPermissions"] = item.GroupPermissions.ToString();
            ret["ID"] = item.ID.ToString();
            ret["InvType"] = item.InvType.ToString();
            ret["Name"] = item.Name.ToString();
            ret["NextPermissions"] = item.NextPermissions.ToString();
            ret["Owner"] = item.Owner.ToString();
            ret["SalePrice"] = item.SalePrice.ToString();
            ret["SaleType"] = item.SaleType.ToString();

            return ret;
        }

        private InventoryFolderBase BuildFolder(Dictionary<string,object> data)
        {
            InventoryFolderBase folder = new InventoryFolderBase();

            folder.ParentID =  new UUID(data["ParentID"].ToString());
            folder.Type = short.Parse(data["Type"].ToString());
            folder.Version = ushort.Parse(data["Version"].ToString());
            folder.Name = data["Name"].ToString();
            folder.Owner =  new UUID(data["Owner"].ToString());
            folder.ID = new UUID(data["ID"].ToString());

            return folder;
        }

        private InventoryItemBase BuildItem(Dictionary<string,object> data)
        {
            InventoryItemBase item = new InventoryItemBase();

            item.AssetID = new UUID(data["AssetID"].ToString());
            item.AssetType = int.Parse(data["AssetType"].ToString());
            item.Name = data["Name"].ToString();
            item.Owner = new UUID(data["Owner"].ToString());
            item.ID = new UUID(data["ID"].ToString());
            item.InvType = int.Parse(data["InvType"].ToString());
            item.Folder = new UUID(data["Folder"].ToString());
            item.CreatorId = data["CreatorId"].ToString();
            item.CreatorData = data["CreatorData"].ToString();
            item.Description = data["Description"].ToString();
            item.NextPermissions = uint.Parse(data["NextPermissions"].ToString());
            item.CurrentPermissions = uint.Parse(data["CurrentPermissions"].ToString());
            item.BasePermissions = uint.Parse(data["BasePermissions"].ToString());
            item.EveryOnePermissions = uint.Parse(data["EveryOnePermissions"].ToString());
            item.GroupPermissions = uint.Parse(data["GroupPermissions"].ToString());
            item.GroupID = new UUID(data["GroupID"].ToString());
            item.GroupOwned = bool.Parse(data["GroupOwned"].ToString());
            item.SalePrice = int.Parse(data["SalePrice"].ToString());
            item.SaleType = byte.Parse(data["SaleType"].ToString());
            item.Flags = uint.Parse(data["Flags"].ToString());
            item.CreationDate = int.Parse(data["CreationDate"].ToString());

            return item;
        }

    }
    /// <summary>
    /// Public HG containment, not authentication. Uses home-side travel records.
    /// Raw private inventory is used only for read-only ownership/collision checks.
    /// </summary>
    public sealed class HGInventoryTravelGate
    {
        private readonly IHGTravelingData m_Travel;
        private readonly IInventoryService m_LocalInventory;
        private readonly HashSet<string> m_HomeURLs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HGInventoryTravelGate(IConfigSource config)
        {
            IConfig db = config.Configs["DatabaseService"];
            IConfig uas = config.Configs["UserAgentService"];
            string provider = db == null ? "" : db.GetString("StorageProvider", "");
            string connection = db == null ? "" : db.GetString("ConnectionString", "");
            string realm = "hg_traveling_data";
            if (uas != null)
            {
                provider = uas.GetString("StorageProvider", provider);
                connection = uas.GetString("ConnectionString", connection);
                realm = uas.GetString("Realm", realm);
            }
            m_Travel = ServerUtils.LoadPlugin<IHGTravelingData>(provider, new object[] { connection, realm });
            if (m_Travel == null)
                throw new Exception("HG inventory gate cannot load the travel database");

            IConfig inv = config.Configs["InventoryService"];
            if (inv == null)
                throw new Exception("HG inventory gate requires [InventoryService]");
            m_LocalInventory = ServerUtils.LoadPlugin<IInventoryService>(
                inv.GetString("LocalServiceModule", ""), new object[] { config, "InventoryService" });
            if (m_LocalInventory == null)
                throw new Exception("HG inventory gate cannot load local inventory for ownership checks");

            string home = Util.GetConfigVarFromSections<string>(config, "GatekeeperURI",
                new string[] { "Startup", "Hypergrid", "UserAgentService" }, String.Empty);
            if (String.IsNullOrWhiteSpace(home) && uas != null)
                home = uas.GetString("ExternalName", "");
            IConfig keeper = config.Configs["GatekeeperService"];
            if (String.IsNullOrWhiteSpace(home) && keeper != null)
                home = keeper.GetString("ExternalName", "");
            AddHome(home);
            IConfig constants = config.Configs["Const"];
            if (constants != null)
            {
                string baseURL = constants.GetString("BaseURL", "");
                Uri uri;
                int port;
                if (Uri.TryCreate(baseURL, UriKind.Absolute, out uri) &&
                    Int32.TryParse(constants.GetString("PublicPort", "8002"), out port))
                    AddHome(new UriBuilder(uri) { Port = port }.Uri.AbsoluteUri);
            }
            IConfig hg = config.Configs["Hypergrid"];
            if (hg != null)
                foreach (string alias in hg.GetString("GatekeeperURIAlias", "").Split(','))
                    if (!String.IsNullOrWhiteSpace(alias))
                        AddHome(alias.Contains("://") ? alias.Trim() : "http://" + alias.Trim());
            IConfig publicInv = config.Configs["HGInventoryService"];
            if (publicInv != null)
                foreach (string alias in publicInv.GetString("InventoryGateHomeURIs", "").Split(','))
                    if (!String.IsNullOrWhiteSpace(alias)) AddHome(alias.Trim());
            if (m_HomeURLs.Count == 0)
                throw new Exception("HG inventory gate requires a valid home GatekeeperURI");
        }

        private static string Normalize(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                return null;
            return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() + uri.AbsolutePath.TrimEnd('/');
        }

        private void AddHome(string value)
        {
            string normalized = Normalize(value);
            if (normalized != null) m_HomeURLs.Add(normalized);
        }

        private bool IsAbroad(UUID owner)
        {
            if (owner == UUID.Zero) return false;
            HGTravelingData[] visits = m_Travel.GetSessions(owner);
            if (visits == null) return false;
            foreach (HGTravelingData visit in visits)
            {
                string destination;
                if (visit.UserID != owner || visit.SessionID == UUID.Zero || visit.Data == null ||
                    !visit.Data.TryGetValue("GridExternalName", out destination)) continue;
                string normalized = Normalize(destination);
                if (normalized != null && !m_HomeURLs.Contains(normalized)) return true;
            }
            return false;
        }

        private static UUID Required(Dictionary<string, object> request, string key)
        {
            object value;
            UUID id;
            if (!request.TryGetValue(key, out value) || value == null ||
                !UUID.TryParse(value.ToString(), out id) || id == UUID.Zero)
                throw new ArgumentException("missing-or-invalid-" + key);
            return id;
        }

        private static List<UUID> IDs(Dictionary<string, object> request, string key)
        {
            object value;
            if (!request.TryGetValue(key, out value) || value == null)
                throw new ArgumentException("missing-" + key);
            IEnumerable<string> values = value as List<string>;
            if (values == null) values = value.ToString().Split(',');
            List<UUID> ids = new List<UUID>();
            foreach (string text in values)
            {
                UUID id;
                if (!UUID.TryParse(text, out id) || id == UUID.Zero)
                    throw new ArgumentException("invalid-" + key);
                ids.Add(id);
            }
            if (ids.Count == 0) throw new ArgumentException("empty-" + key);
            return ids;
        }

        private void Folder(UUID owner, UUID id)
        {
            InventoryFolderBase folder = m_LocalInventory.GetFolder(owner, id);
            if (folder == null || folder.Owner != owner)
                throw new ArgumentException("folder-owner-mismatch");
        }

        private InventoryItemBase Item(UUID owner, UUID id)
        {
            InventoryItemBase item = m_LocalInventory.GetItem(owner, id);
            if (item == null || item.Owner != owner)
                throw new ArgumentException("item-owner-mismatch");
            Folder(owner, item.Folder);
            return item;
        }

        public bool Check(string method, Dictionary<string, object> request, out string reason)
        {
            reason = "foreign-travel-record";
            try
            {
                // Match the always-restricted suitcase policy, including its no-delete behavior.
                if (method == "CREATEUSERINVENTORY" || method == "DELETEITEMS" ||
                    method == "DELETEFOLDERS" || method == "PURGEFOLDER")
                    throw new ArgumentException("operation-disabled-on-public-HG");

                bool suppliedOwner = method == "ADDITEM" || method == "UPDATEITEM" ||
                    method == "ADDFOLDER" || method == "UPDATEFOLDER";
                UUID owner;
                // Legacy single-item/folder reads may omit PRINCIPAL. Derive owner from stored data.
                object claimedPrincipal;
                UUID parsedPrincipal = UUID.Zero;
                bool principalPresent = request.TryGetValue("PRINCIPAL", out claimedPrincipal);
                if (principalPresent && (claimedPrincipal == null ||
                    !UUID.TryParse(claimedPrincipal.ToString(), out parsedPrincipal)))
                    throw new ArgumentException("invalid-PRINCIPAL");
                if ((method == "GETITEM" || method == "GETFOLDER") && parsedPrincipal == UUID.Zero)
                {
                    UUID id = Required(request, "ID");
                    if (method == "GETITEM")
                    {
                        InventoryItemBase item = m_LocalInventory.GetItem(UUID.Zero, id);
                        if (item == null) throw new ArgumentException("item-not-found");
                        owner = item.Owner;
                    }
                    else
                    {
                        InventoryFolderBase folder = m_LocalInventory.GetFolder(UUID.Zero, id);
                        if (folder == null) throw new ArgumentException("folder-not-found");
                        owner = folder.Owner;
                    }
                }
                else owner = Required(request, suppliedOwner ? "Owner" : "PRINCIPAL");
                if (!IsAbroad(owner)) throw new ArgumentException("no-foreign-travel-record");

                switch (method)
                {
                    case "GETINVENTORYSKELETON": case "GETROOTFOLDER": case "GETFOLDERFORTYPE":
                    case "GETACTIVEGESTURES": case "GETASSETPERMISSIONS": break;
                    case "GETITEM": Item(owner, Required(request, "ID")); break;
                    case "GETFOLDER": Folder(owner, Required(request, "ID")); break;
                    case "GETFOLDERCONTENT": case "GETFOLDERITEMS":
                        Folder(owner, Required(request, "FOLDER")); break;
                    case "GETMULTIPLEITEMS":
                        foreach (UUID id in IDs(request, "ITEMS")) Item(owner, id);
                        break;
                    case "GETMULTIPLEFOLDERSCONTENT":
                        List<UUID> folders = IDs(request, "FOLDERS");
                        if (Int32.Parse(request["COUNT"].ToString()) != folders.Count)
                            throw new ArgumentException("folder-count-mismatch");
                        foreach (UUID id in folders) Folder(owner, id);
                        break;
                    case "ADDITEM":
                        Folder(owner, Required(request, "Folder"));
                        if (m_LocalInventory.GetItem(UUID.Zero, Required(request, "ID")) != null)
                            throw new ArgumentException("additem-existing-UUID");
                        break;
                    case "UPDATEITEM":
                        InventoryItemBase original = Item(owner, Required(request, "ID"));
                        if (original.Folder != Required(request, "Folder"))
                            throw new ArgumentException("updateitem-folder-change");
                        break;
                    case "ADDFOLDER":
                        Folder(owner, Required(request, "ParentID"));
                        if (m_LocalInventory.GetFolder(UUID.Zero, Required(request, "ID")) != null)
                            throw new ArgumentException("addfolder-existing-UUID");
                        break;
                    case "UPDATEFOLDER": case "MOVEFOLDER":
                        UUID folderID = Required(request, "ID");
                        Folder(owner, folderID);
                        UUID parentID = Required(request, "ParentID");
                        Folder(owner, parentID);
                        // Reject cycles; also blocks moving an ancestor below itself.
                        HashSet<UUID> seen = new HashSet<UUID>();
                        UUID ancestor = parentID;
                        while (ancestor != UUID.Zero)
                        {
                            if (ancestor == folderID || !seen.Add(ancestor))
                                throw new ArgumentException("folder-cycle");
                            InventoryFolderBase f = m_LocalInventory.GetFolder(owner, ancestor);
                            if (f == null || f.Owner != owner)
                                throw new ArgumentException("invalid-parent-chain");
                            ancestor = f.ParentID;
                        }
                        // UPDATEFOLDER must not bypass the suitcase MoveFolder scope checks.
                        if (method == "UPDATEFOLDER")
                        {
                            InventoryFolderBase old = m_LocalInventory.GetFolder(owner, folderID);
                            if (old.ParentID != parentID)
                                throw new ArgumentException("updatefolder-parent-change-use-movefolder");
                        }
                        break;
                    case "MOVEITEMS":
                        List<UUID> items = IDs(request, "IDLIST");
                        List<UUID> destinations = IDs(request, "DESTLIST");
                        if (items.Count != destinations.Count)
                            throw new ArgumentException("move-count-mismatch");
                        for (int i = 0; i < items.Count; ++i)
                        { Item(owner, items[i]); Folder(owner, destinations[i]); }
                        break;
                    default: throw new ArgumentException("unknown-method");
                }
                return true;
            }
            catch (ArgumentException e)
            {
                reason = e.Message;
                return false;
            }
            catch (Exception)
            {
                reason = "validation-error"; // Fail closed; no credentials/connection strings in audit.
                return false;
            }
        }
    }

}
