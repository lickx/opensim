/* 11 feb 2018
 * 
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Web;
using Mono.Addins;
using OpenSim.Framework.Monitoring;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Capabilities.Handlers;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GetMeshModule")]
    public class GetMeshModule : INonSharedRegionModule
    {
        struct aPollRequest
        {
            public PollServiceMeshEventArgs thepoll;
            public UUID reqID;
            public Hashtable request;
        }

        public class aPollResponse
        {
            public Hashtable response;
            public int bytes;
            public int lod;
        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        private string m_URL;

        private string m_URL2;
        private string m_RedirectURL = null;
        private string m_RedirectURL2 = null;

        // Removed m_enable boolean since it was always true and never set to false.

        private static IAssetService m_assetService = null;

        private Dictionary<UUID, string> m_capsDict = new Dictionary<UUID, string>();
        private static int m_NumberScenes = 0;

        private static readonly Queue<aPollRequest> m_queue = new Queue<aPollRequest>();
        private static readonly ManualResetEvent m_signal = new ManualResetEvent(true);
        private static readonly object m_queueSync = new object();
        private static volatile bool m_running = true;

        private Dictionary<UUID, PollServiceMeshEventArgs> m_pollservices = new Dictionary<UUID, PollServiceMeshEventArgs>();

        #region Region Module interfaceBase Members

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            m_URL = config.GetString("Cap_GetMesh", string.Empty);
            // Cap doesn't exist
            if (m_URL != string.Empty)
            {
                m_RedirectURL = config.GetString("GetMeshRedirectURL");
            }

            m_URL2 = config.GetString("Cap_GetMesh2", string.Empty);
            // Cap doesn't exist
            if (m_URL2 != string.Empty)
            {
                m_RedirectURL2 = config.GetString("GetMesh2RedirectURL");
            }
        }

        public void AddRegion(Scene pScene)
        {
            m_scene = pScene;

            if (m_assetService == null) // Only need to set this once.
            {
                m_assetService = pScene.AssetService;
            }
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
            m_scene.EventManager.OnDeregisterCaps -= DeregisterCaps;

            m_NumberScenes--;

            m_scene = null;

            if (m_NumberScenes <= 0)
            {
                m_running = false;
            }
        }

        public void RegionLoaded(Scene scene)
        {
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
            m_scene.EventManager.OnDeregisterCaps += DeregisterCaps;

            m_NumberScenes++;
            m_running = true;

            if (m_assetService == null) // Only need to set this once.
            {
                m_assetService = m_scene.AssetService;
            }

            if (m_NumberScenes == 1)
            {
                for (int i = 1; i <= 2; i++)
                {
                    Util.FireAndForget(
                        delegate
                        {
                        // We give each thread its own handler.
                        GetMeshHandler getMeshHandler = new GetMeshHandler(m_assetService);
                            DoMeshRequests(getMeshHandler);
                        }, null,
                        String.Format("GetMeshWorker{0}",i), false);
                }
            }
        }

        public void Close()
        {
            if (m_NumberScenes <= 0)
            {
                m_log.DebugFormat("[GetMeshModule] Closing");

                try
                {
                    lock (m_queueSync)
                    {
                        m_running = false;
                        m_queue.Clear();

                        // Wake the threads so they will notice m_running = false and end.
                        m_signal.Set();
                    }
                }
                catch { }
            }
        }

        public string Name { get { return "GetMeshModule"; } }

        #endregion

        private static bool TryDequeue(out aPollRequest poolreq)
        {
            lock (m_queueSync)
            {
                if (m_running)
                {
                    if (m_queue.Count > 0)
                    {
                        poolreq = m_queue.Dequeue();
                        return true;
                    }
                    
                    try
                    {
                        // Reset flag to wait for a new request.
                        m_signal.Reset();
                    }
                    catch { }
                }
            }

            // Wait until there are new requests.
            // We want to wait outside of the lock.
            if (m_running)
                try
                {
                    m_signal.WaitOne();
                }
                catch { }

            poolreq = new aPollRequest();
            return false;
        }

        private static void DoMeshRequests( GetMeshHandler getHandler )
        {
            while (m_running )
            {
                aPollRequest poolreq;
                if (TryDequeue(out poolreq))
                {
                    try
                    {
                         poolreq.thepoll.Process(poolreq, getHandler);

                        // Make sure the thread stays awake while there are requests.
                        m_signal.Set();
                    }
                    catch { }
                }
            }
        }

        private class PollServiceMeshEventArgs : PollServiceEventArgs
        {
            private List<Hashtable> requests =
                    new List<Hashtable>();
            private Dictionary<UUID, aPollResponse> responses =
                    new Dictionary<UUID, aPollResponse>();

            private Scene m_scene;

            public PollServiceMeshEventArgs(string uri, UUID pId, Scene scene) :
                base(null, uri, null, null, null, pId, int.MaxValue)
            {
                m_scene = scene;
                // x is request id, y is userid
                HasEvents = (x, y) =>
                {
                    lock (responses)
                         return responses.ContainsKey(x);
                };
                GetEvents = (x, y) =>
                {
                    lock (responses)
                    {
                        try
                        {
                            return responses[x].response;
                        }
                        finally
                        {
                            responses.Remove(x);
                        }
                    }
                };
                // x is request id, y is request data hashtable
                Request = (x, y) =>
                {
                    if (x != UUID.Zero)
                    {
                        aPollRequest reqinfo = new aPollRequest();
                        reqinfo.thepoll = this;
                        reqinfo.reqID = x;
                        reqinfo.request = y;

                        lock (m_queueSync)
                        {
                            m_queue.Enqueue(reqinfo);
                            m_signal.Set();
                        }
                    }
                };

                // this should never happen except possible on shutdown
                NoEvents = (x, y) =>
                {
                    Hashtable response = new Hashtable();

                    response["int_response_code"] = 500;
                    response["str_response_string"] = "Script timeout";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;
                    response["reusecontext"] = false;

                    return response;
                };
            }

            public void Process(aPollRequest requestinfo, GetMeshHandler getHandler)
            {
                Hashtable response;

                UUID requestID = requestinfo.reqID;

                if (m_scene.ShuttingDown)
                    return;

                // If the avatar is gone, don't bother to get the mesh
                if (m_scene.GetScenePresence(Id) == null)
                {
                    response = new Hashtable();

                    response["int_response_code"] = 500;
                    response["str_response_string"] = "Script timeout";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;
                    response["reusecontext"] = false;

                    lock (responses)
                        responses[requestID] = new aPollResponse() { bytes = 0, response = response, lod = 0 };

                    return;
                }

                response = getHandler.Handle(requestinfo.request);
                lock (responses)
                {
                    responses[requestID] = new aPollResponse()
                    {
                        bytes = (int)response["int_bytes"],
                        lod = (int)response["int_lod"],
                        response = response,
                    };
                }
            }
        }

        public void RegisterCaps(UUID agentID, Caps caps)
        {
//            UUID capID = UUID.Random();
            if (m_URL == "localhost")
            {
                string capUrl = "/CAPS/" + UUID.Random() + "/";

                // Register this as a poll service
                PollServiceMeshEventArgs args = new PollServiceMeshEventArgs(capUrl, agentID, m_scene);

                args.Type = PollServiceEventArgs.EventType.Mesh;
                MainServer.Instance.AddPollServiceHTTPHandler(capUrl, args);

                string hostName = m_scene.RegionInfo.ExternalHostName;
                uint port = (MainServer.Instance == null) ? 0 : MainServer.Instance.Port;
                string protocol = "http";

                if (MainServer.Instance.UseSSL)
                {
                    hostName = MainServer.Instance.SSLCommonName;
                    port = MainServer.Instance.SSLPort;
                    protocol = "https";
                }
                caps.RegisterHandler("GetMesh", String.Format("{0}://{1}:{2}{3}", protocol, hostName, port, capUrl));
                m_pollservices[agentID] = args;
                m_capsDict[agentID] = capUrl;
            }
            else
            {
                caps.RegisterHandler("GetMesh", m_URL);
            }
        }

        private void DeregisterCaps(UUID agentID, Caps caps)
        {
            string capUrl;
            PollServiceMeshEventArgs args;
            if (m_capsDict.TryGetValue(agentID, out capUrl))
            {
                MainServer.Instance.RemoveHTTPHandler("", capUrl);
                m_capsDict.Remove(agentID);
            }
            if (m_pollservices.TryGetValue(agentID, out args))
            {
                m_pollservices.Remove(agentID);
            }
        }
    }
}
