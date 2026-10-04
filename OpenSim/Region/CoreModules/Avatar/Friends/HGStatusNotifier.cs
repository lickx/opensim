using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Hypergrid;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;

using OpenMetaverse;

using log4net;

namespace OpenSim.Region.CoreModules.Avatar.Friends
{
    public class HGStatusNotifier
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private HGFriendsModule m_FriendsModule;

        public HGStatusNotifier(HGFriendsModule friendsModule)
        {
            m_FriendsModule = friendsModule;
        }

        public void Notify(UUID userID, List<UUID>friendIds, bool online)
        {
            if(m_FriendsModule is null)
                return;

            if (friendIds.Count == 0)
                return; // no one to notify. caller don't do this

            m_log.DebugFormat("[HG STATUS NOTIFIER]: Notifying {0} foreign friends", friendIds.Count);
            foreach (UUID friendID in friendIds)
            {
                string friendsServerURI = m_FriendsModule.UserManagementModule.GetUserServerURL(friendID, "FriendsServerURI");
                if (!string.IsNullOrEmpty(friendsServerURI))
                {
                    HGFriendsServicesConnector fConn = new(friendsServerURI);

                    // One friend per list because other grids can't handle multiple
                    // More overhead, but at least it always works, even on osgrid sims
                    List<UUID> friendsOnline = fConn.StatusNotification(new List<string> { friendID.ToString() }, userID, online);

                    if (friendsOnline.Count > 0)
                    {
                        IClientAPI client = m_FriendsModule.LocateClientObject(userID);
                        if(client is not null)
                        {
                            m_FriendsModule.CacheFriendsOnline(userID, friendsOnline, online);
                            if(online)
                                client.SendAgentOnline(friendsOnline.ToArray());
                            else
                                client.SendAgentOffline(friendsOnline.ToArray());
                        }
                    }
                }
            }
        }

    }
}
