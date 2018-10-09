/* 30 August 2018
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
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;
using OpenSim.Capabilities.Handlers;
using OpenSim.Framework.Monitoring;

namespace OpenSim.Region.ClientStack.Linden
{

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GetTextureModule")]
    public class GetTextureModule : INonSharedRegionModule
    {
        struct aPollRequest
        {
            public PollServiceTextureEventArgs thepoll;
            public UUID reqID;
            public Hashtable request;
        }

        public class aPollResponse
        {
            public Hashtable response;
            public int bytes;
        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        private static IAssetService m_assetService = null;
        
        private Dictionary<UUID, string> m_capsDict = new Dictionary<UUID, string>();
        private static int m_NumberScenes = 0;

        private static Queue<aPollRequest> m_queue = new Queue<aPollRequest>();
        private static ManualResetEvent m_signal = new ManualResetEvent(true);

        private static object m_queueSync = new object();
        private static volatile bool m_running = true;

        private static System.Threading.Timer[] m_queueTimer = new System.Threading.Timer[4] { null, null, null, null };

        private Dictionary<UUID,PollServiceTextureEventArgs> m_pollservices = new Dictionary<UUID,PollServiceTextureEventArgs>();

        private string m_Url = "localhost";

        #region ISharedRegionModule Members

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];

            if (config == null)
                return;

            m_Url = config.GetString("Cap_GetTexture", "localhost");
        }

        public void AddRegion(Scene s)
        {
            m_scene = s;

            if (m_assetService == null) // Only need to set this once.
            {
                m_assetService = s.AssetService;
            }
        }

        public void RemoveRegion(Scene s)
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

        public void RegionLoaded(Scene s)
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
                m_running = true;
                for (int i = 0; i < 4; i++)
                {
                    m_queueTimer[i] = new System.Threading.Timer(
                                           delegate { DoTextureRequests(); },
                                           null, 0, Timeout.Infinite);
                }
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (m_NumberScenes <= 0)
            {
                m_log.DebugFormat("[GetTextureModule] Closing");

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

        public string Name { get { return "GetTextureModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        private class PollServiceTextureEventArgs : PollServiceEventArgs
        {
            private List<Hashtable> requests =
                    new List<Hashtable>();
            private Dictionary<UUID, aPollResponse> responses =
                    new Dictionary<UUID, aPollResponse>();

            private Scene m_scene;

            public PollServiceTextureEventArgs(UUID pId, Scene scene) :
                    base(null, "", null, null, null, null, pId, int.MaxValue)
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

            public void Process(aPollRequest requestinfo, GetTextureHandler getHandler)
            {
                Hashtable response;

                UUID requestID = requestinfo.reqID;

                if(m_scene.ShuttingDown)
                    return;

                // If the avatar is gone, don't bother to get the texture
                if (m_scene.GetScenePresence(Id) == null)
                {
                    response = new Hashtable();

                    response["int_response_code"] = 500;
                    response["str_response_string"] = "Script timeout";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;
                    response["reusecontext"] = false;

                    lock (responses)
                        responses[requestID] = new aPollResponse() {bytes = 0, response = response};

                    return;
                }

                response = getHandler.Handle(requestinfo.request);
                lock (responses)
                {
                    responses[requestID] = new aPollResponse()
                                               {
                                                   bytes = (int)response["int_bytes"],
                                                   response = response,
                                               };
                }
            }
            
        }

        private void RegisterCaps(UUID agentID, Caps caps)
        {
            if (m_Url == "localhost")
            {
                string capUrl = "/CAPS/" + UUID.Random() + "/";

                // Register this as a poll service
                PollServiceTextureEventArgs args = new PollServiceTextureEventArgs(agentID, m_scene);

                args.Type = PollServiceEventArgs.EventType.Texture;
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
                IExternalCapsModule handler = m_scene.RequestModuleInterface<IExternalCapsModule>();
                if (handler != null)
                    handler.RegisterExternalUserCapsHandler(agentID, caps, "GetTexture", capUrl);
                else
                    caps.RegisterHandler("GetTexture", String.Format("{0}://{1}:{2}{3}", protocol, hostName, port, capUrl));
                m_pollservices[agentID] = args;
                m_capsDict[agentID] = capUrl;
            }
            else
            {
                caps.RegisterHandler("GetTexture", m_Url);
            }
        }

        private void DeregisterCaps(UUID agentID, Caps caps)
        {
            PollServiceTextureEventArgs args;

            MainServer.Instance.RemoveHTTPHandler("", m_Url);
            m_capsDict.Remove(agentID);

            if (m_pollservices.TryGetValue(agentID, out args))
            {
                m_pollservices.Remove(agentID);
            }
        }

        private static void DoTextureRequests()
        {
            // We give each thread its own handler.
            GetTextureHandler getHandler = new GetTextureHandler(m_assetService);
            while (m_running)
            {
                try
                {
                    aPollRequest poolreq = new aPollRequest();
                    bool wait = false;
                    lock (m_queueSync)
                    {
                        if (m_queue.Count > 0)
                        {
                            poolreq = m_queue.Dequeue();
                            m_signal.Set();
                        }
                        else
                        {
                            // Reset flag to wait for a new requests.
                            m_signal.Reset();
                            wait = true;
                        }
                    }

                    try
                    {
                        if (wait)
                        {
                            m_signal.WaitOne();
                        }
                        else
                        {
                            poolreq.thepoll.Process(poolreq, getHandler);
                        }
                    }
                    catch { }

                    // Make sure the thread stays awake while there are requests.
                    m_signal.Set();
                }
                catch { }
            }
            // exiting the thread now
            try
            {
                m_signal.Set(); // Wake other threads as well so they will end.
            }
            catch { }
        }
    }
}
