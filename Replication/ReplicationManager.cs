// Copyright ?2014, 2018, Oracle and/or its affiliates. All rights reserved.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License, version 2.0, as
// published by the Free Software Foundation.
//
// This program is also distributed with certain software (including
// but not limited to OpenSSL) that is licensed under separate terms,
// as designated in a particular file or component or in included license
// documentation.  The authors of MySQL hereby grant you an
// additional permission to link the program and your derivative works
// with the separately licensed software that they have included with
// MySQL.
//
// Without limiting anything contained in the foregoing, this file,
// which is part of MySQL Connector/NET, is also subject to the
// Universal FOSS Exception, version 1.0, a copy of which can be found at
// http://oss.oracle.com/licenses/universal-foss-exception.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License, version 2.0, for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin St, Fifth Floor, Boston, MA 02110-1301  USA


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;


namespace MySql.Data.MySqlClient.Replication
{
  /// <summary>
  /// Manager for Replication and Load Balancing features
  /// </summary>
  internal static class ReplicationManager
  {
    private static List<ReplicationServerGroup> groups = new List<ReplicationServerGroup>();
    private static Object thisLock = new Object();
    //private static Dictionary<string, ReplicationServerSelector> selectors = new Dictionary<string, ReplicationServerSelector>();

    static ReplicationManager()
    {
      Groups = groups;

        //#if !NETSTANDARD1_6
        // load up our selectors
        //if (MySqlConfiguration.Settings == null) return;

        //foreach (var group in MySqlConfiguration.Settings.Replication.ServerGroups)
        //{
        //    ReplicationServerGroup g = AddGroup(group.Name, group.GroupType, group.RetryTime);
        //    foreach (var server in group.Servers)
        //        g.AddServer(server.Name, server.IsMaster, server.ConnectionString);
        //}

        string databaseServerName = GetStringValue("DatabaseServerName", "mysql");
        var databaseServerNames = databaseServerName.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
        //如果存在多个服务器，则为mysql组复制
        if (databaseServerNames.Count <= 1)
        {
            return;
        }
        //打乱server信息
        List<string> newDatabaseServerNames = new List<string>();
        Random random = new Random();
        int totalCount = databaseServerNames.Count;
        while (newDatabaseServerNames.Count < totalCount)
        {
            int index = random.Next(totalCount - newDatabaseServerNames.Count);
            if (!newDatabaseServerNames.Contains(databaseServerNames[index]))
            {
                newDatabaseServerNames.Add(databaseServerNames[index]);
                databaseServerNames.RemoveAt(index);
            }
        }

        string databaseGroupName = GetStringValue("DatabaseGroupName", "MySQLGroupReplication");
        ReplicationServerGroup g = AddGroup(databaseGroupName, "", 60);
        foreach (var serverName in newDatabaseServerNames)
        {
            bool isMaster = true;
            string connectionString = GenerateMySqlConnectionString(serverName);
            g.AddServer(serverName, isMaster, connectionString);
        }
        //#endif
    }

    /// <summary>
    /// 简单字符串，账号、密码、数据库、端口、语句执行超时时间都在外层设置
    /// </summary>
    /// <param name="databaseServerName"></param>
    /// <returns></returns>
    internal static string GenerateMySqlConnectionString(string databaseServerName)
    {
        int commandTimeout = GetStringValue("CommandTimeout", 1800);
        string connStr = string.Format("Server={0};Pooling=true;Min Pool Size = 10;Max Pool Size = 32767;Connection Timeout=120;Allow User Variables=true;SslMode=none;Command Timeout={1};", databaseServerName, commandTimeout);
        return connStr;
    }

    internal static string GetStringValue(string name, string defaultValue)
    {
        string str = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(str))
        {
            return defaultValue;
        }
        return str;
    }
    internal static int GetStringValue(string name, int defaultValue)
    {
        string str = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(str))
        {
            return defaultValue;
        }

        int retVal;
        if (!int.TryParse(str, out retVal))
        {
            return defaultValue;
        }

        return retVal;
    }

    /// <summary>
    /// Returns Replication Server Group List
    /// </summary>
    internal static IList<ReplicationServerGroup> Groups { get; private set; }

    /// <summary>
    /// Adds a Default Server Group to the list
    /// </summary>
    /// <param name="name">Group name</param>
    /// <param name="retryTime">Time between reconnections for failed servers</param>
    /// <returns>Replication Server Group added</returns>
    internal static ReplicationServerGroup AddGroup(string name, int retryTime)
    {
      return AddGroup( name, null, retryTime);
    }

    /// <summary>
    /// Adds a Server Group to the list
    /// </summary>
    /// <param name="name">Group name</param>
    /// <param name="groupType">ServerGroup type reference</param>
    /// <param name="retryTime">Time between reconnections for failed servers</param>
    /// <returns>Server Group added</returns>
    internal static ReplicationServerGroup AddGroup(string name, string groupType, int retryTime)
    {
      if (string.IsNullOrEmpty(groupType))
        groupType = "MySql.Data.MySqlClient.Replication.ReplicationRoundRobinServerGroup";
      Type t = Type.GetType(groupType);
      ReplicationServerGroup g = (ReplicationServerGroup)Activator.CreateInstance(t, name, retryTime) as ReplicationServerGroup;
      groups.Add(g);
      return g;
    }

    /// <summary>
    /// Gets the next server from a replication group
    /// </summary>
    /// <param name="groupName">Group name</param>
    /// <param name="isMaster">True if the server to return must be a master</param>
    /// <returns>Replication Server defined by the Load Balancing plugin</returns>
    internal static ReplicationServer GetServer(string groupName, bool isMaster)
    {
      ReplicationServerGroup group = GetGroup(groupName);
      return group.GetServer(isMaster);
    }

    /// <summary>
    /// Gets a Server Group by name
    /// </summary>
    /// <param name="groupName">Group name</param>
    /// <returns>Server Group if found, otherwise throws an MySqlException</returns>
    internal static ReplicationServerGroup GetGroup(string groupName)
    {
      ReplicationServerGroup group = null;
      foreach (ReplicationServerGroup g in groups)
      {
        if (String.Compare(g.Name, groupName, StringComparison.OrdinalIgnoreCase) != 0) continue;
        group = g;
        break;
      }
      if (group == null)
        throw new MySqlException(String.Format(Resources.ReplicationGroupNotFound, groupName));
      return group;
    }

    /// <summary>
    /// Validates if the replication group name exists
    /// </summary>
    /// <param name="groupName">Group name to validate</param>
    /// <returns><c>true</c> if the replication group name is found; otherwise, <c>false</c></returns>
    internal static bool IsReplicationGroup(string groupName)
    {
      foreach (ReplicationServerGroup g in groups)
        if (String.Compare(g.Name, groupName, StringComparison.OrdinalIgnoreCase) == 0) return true;
      return false;
    }

    /// <summary>
    /// Assigns a new server driver to the connection object
    /// </summary>
    /// <param name="groupName">Group name</param>
    /// <param name="master">True if the server connection to assign must be a master</param>
    /// <param name="connection">MySqlConnection object where the new driver will be assigned</param>
    internal static void GetNewConnection(string groupName, bool master, MySqlConnection connection)
    {
      do
      {
        lock (thisLock)
        {
          if (!IsReplicationGroup(groupName)) return;

          ReplicationServerGroup group = GetGroup(groupName);
          ReplicationServer server = group.GetServer(master, connection.Settings);

          if (server == null)
            throw new MySqlException(Resources.Replication_NoAvailableServer);

          //根据主连接字符串配置的账号、密码、数据库、端口，使用新的server的连接字符串（不更新，每次都添加）
          string newServerConnectionString = $"{server.ConnectionString}uid={connection.Settings.UserID};pwd={connection.Settings.Password};Database={connection.Settings.Database};port={connection.Settings.Port};";

          try
          {
            bool isNewServer = false;
            if (connection.driver == null || !connection.driver.IsOpen)
            {
              isNewServer = true;
            }
            else
            { 
              MySqlConnectionStringBuilder msb = new MySqlConnectionStringBuilder(newServerConnectionString);
              if (!msb.Equals(connection.driver.Settings))
              {
                isNewServer = true;
              }
            }
            if (isNewServer)
            {
              var builder = new MySqlConnectionStringBuilder(newServerConnectionString);
              MySqlPool pool = MySqlPoolManager.GetPool(builder);
              Driver driver = pool.GetConnection();
              //Driver driver = Driver.Create(new MySqlConnectionStringBuilder(newServerConnectionString));
              connection.driver = driver;
            }
            return;
          }
          catch (MySqlException ex)
          {
            connection.driver = null;
            MySqlTrace.LogError(ex.Number, ex.ToString());
            if (ex.Number == 1042)
            {
              //只有连不上的时候才标记为不可用
              server.IsAvailable = false;
              //连接失败时打印日志（故障情况）
              Console.WriteLine("GetNewConnection Before HandleFailover >> Error:{0},{1}Number={2},ConnectionString={3}", ex.Message, ex.InnerException == null ? "" : "InnerException:" + ex.InnerException.Message + ",", ex.Number, newServerConnectionString);
              server.ConnectionString = newServerConnectionString;
              // retry to open a failed connection and update its status
              group.HandleFailover(server, ex);
            }
            else
              throw;
          }
        }
      } while (true);
    }
  }
}
