﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RaptorDB.Common;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace RaptorDB
{
    public class RaptorDBServer 
    {
        public RaptorDBServer(int port, string DataPath)
        {
            _path = Directory.GetCurrentDirectory();
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
            _server = new NetworkServer();

            _raptor = RaptorDB.Open(DataPath);
            register = _raptor.GetType().GetMethod("RegisterView", BindingFlags.Instance | BindingFlags.Public);
            save = _raptor.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);
            Initialize();
            _server.Start(port, processpayload);
        }

        void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            //perform cleanup here
            log.Debug("process exited");
            Shutdown();   
        }

        private string _S = Path.DirectorySeparatorChar.ToString();
        private Dictionary<string, uint> _users = new Dictionary<string, uint>();
        private string _path = "";
        private ILog log = LogManager.GetLogger(typeof(RaptorDBServer));
        private NetworkServer _server;
        private RaptorDB _raptor;
        private MethodInfo register = null;
        private MethodInfo save = null;
        private SafeDictionary<Type, MethodInfo> _savecache = new SafeDictionary<Type, MethodInfo>();
        private SafeDictionary<string, ServerSideFunc> _ssidecache = new SafeDictionary<string, ServerSideFunc>();

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (File.Exists(args.Name))
                return Assembly.LoadFrom(args.Name);
            string[] ss = args.Name.Split(',');
            string fname = ss[0] + ".dll";
            if (File.Exists(fname))
                return Assembly.LoadFrom(fname);
            fname = "Extensions" + _S + fname;
            if (File.Exists(fname))
                return Assembly.LoadFrom(fname);
            else return null;
        }

        private MethodInfo GetSave(Type type)
        {
            MethodInfo m = null;
            if (_savecache.TryGetValue(type, out m))
                return m;

            m = save.MakeGenericMethod(new Type[] { type });
            _savecache.Add(type, m);
            return m;
        }

        public void Shutdown()
        {
            WriteUsers();
            _server.Stop();
        }

        private void WriteUsers()
        {
            // write users to user.config file
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# FORMAT : username , pasword hash");
            sb.AppendLine("# To disable a user comment the line with the '#'");
            foreach (var kv in _users)
            {
                sb.AppendLine(kv.Key + " , " + kv.Value);
            }

            File.WriteAllText(_path + _S + "RaptorDB-Users.config", sb.ToString());
        }

        private object processpayload(object data)
        {
            Packet p = (Packet)data;

            if (Authenticate(p) == false)
            {
                //JSR - introduce an Authenticate Command
                switch (p.Command)
                {
                    case "authenticate":
                        //This is true as the ReturnPacket is valid, but includes a message.
                        var rtn = new ReturnPacket(true, "Authentication failed");
                        rtn.OK = true;
                        rtn.Data = "Some Dummy Data";
                        return rtn;
                        //break;
                }
                //JSR - cater for the original situation where some other command is executed.
                return new ReturnPacket(false, "Authentication failed");
            }

            ReturnPacket ret = new ReturnPacket(true); 
            try
            {
                object[] param = null;

                switch (p.Command)
                {
                    case "save":
                        var m = GetSave(p.Data.GetType());
                        m.Invoke(_raptor, new object[] { p.Docid, p.Data });
                        break;
                    case "savebytes":
                        ret.OK = _raptor.SaveBytes(p.Docid, (byte[])p.Data);
                        break;
                    case "querytype":
                        param = (object[])p.Data;

                        //Since the RowSchema is nested and used like SalesInvoiceView.RowSchema
                        //the DeclaringType is used.
                        Type t = Type.GetType((string) param[0]);

                        var a = t.Name; //RowSchema
                        var b = t.DeclaringType.Name; //SalesInvoiceView
                        var c = t.ReflectedType.Name; //SalesInvoiceView
                        var d = t.FullName; //RaptorDBTest1Views.SalesInvoiceView+RowSchema

                        var viewname = t.DeclaringType.Name;
                        if (String.IsNullOrEmpty(viewname))
                        {
                            ret.OK = false;
                            ret.Error = "View Not Found.";
                            ret.Data = "Some Dummy Data";
                        }
                        else
                        {
                            ret.OK = true;
                            ret.Data = _raptor.Query(viewname, (string)param[1], p.Start, p.Count, p.OrderBy);
                        }
                        break;
                    case "querystr":
                        ret.OK = true;
                        ret.Data = _raptor.Query(p.Viewname, (string)p.Data, p.Start, p.Count, p.OrderBy);
                        break;
                    case "fetch":
                        ret.OK = true;
                        ret.Data = _raptor.Fetch(p.Docid);
                        break;
                    case "fetchbytes":
                        ret.OK = true;
                        ret.Data = _raptor.FetchBytes(p.Docid);
                        break;
                    case "backup":
                        ret.OK = _raptor.Backup();
                        break;
                    case "delete":
                        ret.OK = _raptor.Delete(p.Docid);
                        break;
                    case "deletebytes":
                        ret.OK = _raptor.DeleteBytes(p.Docid);
                        break;
                    case "restore":
                        ret.OK = true;
                        Task.Factory.StartNew(() => _raptor.Restore());
                        break;
                    case "adduser":
                        param = (object[])p.Data;
                        ret.OK = AddUser((string)param[0], (string)param[1], (string)param[2]);
                        break;
                    case "getusers":
                        ret.OK = true;
                        List<String> users = new List<string>();
                        foreach(var user in _users)
                            users.Add(user.Key);
                        //Only seems to work passing back an Object[]
                        ret.Data = users.ToArray<Object>();
                        break;
                    case "serverside":
                        param = (object[])p.Data;
                        ret.OK = true;
                        ret.Data = _raptor.ServerSide(GetServerSideFuncCache(param[0].ToString(), param[1].ToString()), param[2].ToString());
                        break;
                    case "fulltext":
                        param = (object[])p.Data;
                        ret.OK = true;
                        ret.Data = _raptor.FullTextSearch("" + param[0]);
                        break;
                    case "counttype":
                        // count type
                        param = (object[])p.Data;
                        Type tt = Type.GetType((string)param[0]);
                        string viewname2 = _raptor.GetViewName(tt);
                        //if (viewname2 == "") viewname2 = _raptor.GetView((string)param[0]);
                        ret.OK = true;
                        ret.Data = _raptor.Count(viewname2, (string)param[1]);
                        break;
                    case "countstr":
                        // count str
                        ret.OK = true;
                        ret.Data = _raptor.Count(p.Viewname, (string)p.Data);
                        break;
                    case "gcount":
                        //param = (object[])p.Data;
                        Type ttt = Type.GetType(p.Viewname);
                        string viewname3 = _raptor.GetViewName(ttt);
                        //if (viewname3 == "") viewname3 = _raptor.GetView(p.Viewname);
                        ret.OK = true;
                        ret.Data = _raptor.Count(viewname3, (string)p.Data);
                        break;
                    case "dochistory":
                        ret.OK = true;
                        ret.Data = _raptor.FetchHistory(p.Docid);
                        break;
                    case "filehistory":
                        ret.OK = true;
                        ret.Data = _raptor.FetchBytesHistory(p.Docid);
                        break;
                    case "fetchversion":
                        ret.OK = true;
                        ret.Data = _raptor.FetchVersion((int)p.Data);
                        break;
                    case "fetchfileversion":
                        ret.OK = true;
                        ret.Data = _raptor.FetchBytesVersion((int)p.Data);
                        break;
                    case "checkassembly":
                        ret.OK = true;
                        string typ = "";
                        ret.Data = _raptor.GetAssemblyForView(p.Viewname, out typ);
                        ret.Error = typ;
                        break;
                    case "getviews":
                        ret.OK = true;
                        List<String> views = new List<string>();
                        foreach(var view in _raptor.GetViews())
                            views.Add(view.Name);
                        ret.Data = views.ToArray<Object>();
                        break;
                }
            }
            catch (Exception ex)
            {
                ret.OK = false;
                //JSR
                //
                ret.Error = ex.Message;
                log.Error(ex);
            }
            return ret;
        }

        private ServerSideFunc GetServerSideFuncCache(string type, string method)
        {
            ServerSideFunc func;
            log.Debug("Calling Server side Function : " + method + " on type " + type);
            if (_ssidecache.TryGetValue(type + method, out func) == false)
            {
                Type tt = Type.GetType(type);

                func = (ServerSideFunc)Delegate.CreateDelegate(typeof(ServerSideFunc), tt, method);
                _ssidecache.Add(type + method, func);
            }
            return func;
        }

        private uint GenHash(string user, string pwd)
        {
            return Helper.MurMur.Hash(Encoding.UTF8.GetBytes(user.ToLower() + "|" + pwd));
        }

        private bool AddUser(string user, string oldpwd, string newpwd)
        {
            uint hash = 0;
            if (_users.TryGetValue(user.ToLower(), out hash) == false)
            {
                _users.Add(user.ToLower(), GenHash(user, newpwd));
                return true;
            }
            if (hash == GenHash(user, oldpwd))
            {
                _users[user.ToLower()] = GenHash(user, newpwd);
                return true;
            }
            return false;
        }

        private bool Authenticate(Packet p)
        {
            uint pwd;
            if (_users.TryGetValue(p.Username.ToLower(), out pwd))
            {
                uint hash = uint.Parse(p.PasswordHash);
                if (hash == pwd) return true;
            }
            log.Debug("Authentication failed for '" + p.Username + "' hash = " + p.PasswordHash);
            return false;
        }

        private void Initialize()
        {
            // load users here
            if (File.Exists(_path + _S + "RaptorDB-Users.config"))
            {
                foreach (string line in File.ReadAllLines(_path + _S + "RaptorDB-Users.config"))
                {
                    if (line.Contains("#") == false)
                    {
                        string[] s = line.Split(',');
                        _users.Add(s[0].Trim().ToLower(), uint.Parse(s[1].Trim()));
                    }
                }
            }
            // add default admin user if not exists
            if (_users.ContainsKey("admin") == false)
                _users.Add("admin", GenHash("admin", "admin"));

            // exe folder
            // |-Extensions
            Directory.CreateDirectory(_path + _S + "Extensions");

            // open extensions folder
            string path = _path + _S + "Extensions";

            foreach (var f in Directory.GetFiles(path, "*.dll"))
            {
                //        - load all dll files
                //        - register views 
                log.Debug("loading dll for views : " + f);
                Assembly a = Assembly.Load(f);
                foreach (var t in a.GetTypes())
                {
                    foreach (var att in t.GetCustomAttributes(typeof(RegisterViewAttribute), false))
                    {
                        try
                        {
                            object o = Activator.CreateInstance(t);
                            //  handle types when view<T> also
                            Type[] args = t.GetGenericArguments();
                            if (args.Length == 0)
                                args = t.BaseType.GetGenericArguments();
                            Type tt = args[0];
                            var m = register.MakeGenericMethod(new Type[] { tt });
                            m.Invoke(_raptor, new object[] { o });
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                        }
                    }
                }
            }
        }
    }
}
