﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RaptorDB.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.IO;

namespace RaptorDB
{
    public class RaptorDBClient : IRaptorDB
    {
        public RaptorDBClient(string server, int port, string username, string password)
        {
            _username = username;
            _password = password;
            _client = new NetworkClient(server, port);

            //Authenticate
            Packet p = CreatePacket();
            p.Command = "authenticate";
            //p.Docid = null;
            //p.Data = null;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
            if (ret == null)
            {
                //Don't cast, just retrieve the error
                LastErrorMessage = _client.LastErrorMessage;
                return;
            }

            LastErrorMessage = ret.Error;
            //Would be nice to pass back the Connected State.
        }

        private NetworkClient _client;
        private string _username;
        private string _password;
        private SafeDictionary<string, bool> _assembly = new SafeDictionary<string, bool>();

        public string LastErrorMessage { get; set; }

        /// <summary>
        /// Save a document to RaptorDB
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="docID"></param>
        /// <param name="document"></param>
        /// <returns></returns>
        public bool Save<T>(Guid docID, T document)
        {
            Packet p = CreatePacket();
            p.Command = "save";
            p.Docid = docID;
            p.Data = document;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
            if(ret == null)
                return false;
            return ret.OK;
        }

        /// <summary>
        /// Save a file to RaptorDB
        /// </summary>
        /// <param name="fileID"></param>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public bool SaveBytes(Guid fileID, byte[] bytes)
        {
            Packet p = CreatePacket();
            p.Command = "savebytes";
            p.Docid = fileID;
            p.Data = bytes;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
            if (ret == null)
                return false;
            return ret.OK;
        }

        /// <summary>
        /// Query any view -> get all rows
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="viewname"></param>
        /// <returns></returns>
        public Result<object> Query(string viewname)
        {
            return Query(viewname, 0, -1);
        }

        ///// <summary>
        ///// Query a primary view -> get all rows
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="view"></param>
        ///// <returns></returns>
        //public Result<object> Query(Type type)
        //{
        //    return Query(type, 0, -1);
        //}

        /// <summary>
        /// Query a view using a string filter
        /// </summary>
        /// <param name="viewname"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public Result<object> Query(string viewname, string filter)
        {
            return Query(viewname, filter, 0, -1);
        }

        //// FEATURE : add paging to queries -> start, count
        ///// <summary>
        ///// Query any view with filters
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="viewname">view name</param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public Result<object> Query<T>(string viewname, Expression<Predicate<T>> filter)
        //{
        //    return Query(viewname, filter, 0, -1);
        //}

        ///// <summary>
        ///// Query a view with filters
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="view">base entity type, or typeof the view </param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public Result<object> Query<T>(Type view, Expression<Predicate<T>> filter)
        //{
        //    return Query<T>(view, filter, 0, -1);
        //}

        ///// <summary>
        ///// Query a view with filters
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="view">base entity type, or typeof the view </param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public Result<object> Query(Type view, string filter)
        //{
        //    return Query(view, filter, 0, -1);
        //}

        /// <summary>
        /// Fetch a document by it's ID
        /// </summary>
        /// <param name="docID"></param>
        /// <returns></returns>
        public object Fetch(Guid docID)
        {
            Packet p = CreatePacket();
            p.Command = "fetch";
            p.Docid = docID;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return ret.Data;
            else
                return null;
        }

        /// <summary>
        /// Fetch file data by it's ID
        /// </summary>
        /// <param name="fileID"></param>
        /// <returns></returns>
        public byte[] FetchBytes(Guid fileID)
        {
            Packet p = CreatePacket();
            p.Command = "fetchbytes";
            p.Docid = fileID;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
            
            if (ret == null)
                return null;

            if (ret.OK)
                return (byte[]) ret.Data;
            else
                return null;
        }

        public Object[] GetViews()
        {
            Packet p = CreatePacket();
            p.Command = "getviews";
            ReturnPacket ret = (ReturnPacket) _client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return  (Object[]) ret.Data;
            else
                return null;
        }

        /// <summary>
        /// Shutdown and cleanup 
        /// </summary>
        public void Shutdown()
        {
            _client.Close();
        }

        /// <summary>
        /// Backup the data file in incremental mode to the RaptorDB folder
        /// </summary>
        /// <returns></returns>
        public bool Backup()
        {
            Packet p = CreatePacket();
            p.Command = "backup";
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return false;
            
            return ret.OK;
        }

        /// <summary>
        /// Restore backup files stored in RaptorDB folder
        /// </summary>
        public bool Restore2()
        {
            Packet p = CreatePacket();
            p.Command = "restore";
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return false;

            return ret.OK;
        }

        /// <summary>
        /// Restore backup files stored in RaptorDB folder
        /// </summary>
        public void Restore()
        {
            Packet p = CreatePacket();
            p.Command = "restore";
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
        }

        /// <summary>
        /// Delete a document (the actual data is not deleted just marked so) 
        /// </summary>
        /// <param name="docid"></param>
        /// <returns></returns>
        public bool Delete(Guid docid)
        {
            Packet p = CreatePacket();
            p.Command = "delete";
            p.Docid = docid;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);
            
            if (ret == null)
                return false;

            return ret.OK;
        }

        /// <summary>
        /// Delete a file (the actual data is not deleted just marked so) 
        /// </summary>
        /// <param name="fileid"></param>
        /// <returns></returns>
        public bool DeleteBytes(Guid fileid)
        {
            Packet p = CreatePacket();
            p.Command = "deletebytes";
            p.Docid = fileid;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return false;

            return ret.OK;
        }

        /// <summary>
        /// Add a user for server mode login
        /// </summary>
        /// <param name="username"></param>
        /// <param name="oldpassword"></param>
        /// <param name="newpassword"></param>
        /// <returns></returns>
        public bool AddUser(string username, string oldpassword, string newpassword)
        {
            Packet p = CreatePacket();
            p.Command = "adduser";
            p.Data = new object[] { username, oldpassword, newpassword };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return false;

            return ret.OK;
        }

        public Object[] GetUsers()
        {
            Packet p = CreatePacket();
            p.Command = "getusers";
            p.Data = "Some Dummy Data";
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (Object[]) ret.Data;
            else
                return null;
        }

        /// <summary>
        /// Execute server side queries
        /// </summary>
        /// <param name="func"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public object[] ServerSide(ServerSideFunc func, string filter)
        {
            Packet p = CreatePacket();
            p.Command = "serverside";
            p.Data = new object[] { func.Method.ReflectedType.AssemblyQualifiedName, func.Method.Name, filter };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (object[]) ret.Data;
            else
                return null;
        }

        /// <summary>
        /// Execute server side queries
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="func"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public object[] ServerSide<T>(ServerSideFunc func, Expression<Predicate<T>> filter)
        {
            LINQString ls = new LINQString();
            ls.Visit(filter);

            Packet p = CreatePacket();
            p.Command = "serverside";
            p.Data = new object[] { func.Method.ReflectedType.AssemblyQualifiedName, func.Method.Name, ls.sb.ToString() };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (object[])ret.Data;
            else
                return null;
        }

        /// <summary>
        /// Full text search the complete original document 
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public int[] FullTextSearch(string filter)
        {
            Packet p = CreatePacket();
            p.Command = "fulltext";
            p.Data = new object[] { filter };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (int[]) ret.Data;
            else
                return null;
        }

        private Packet CreatePacket()
        {
            Packet p = new Packet();
            p.Username = _username;
            p.PasswordHash = Helper.MurMur.Hash(Encoding.UTF8.GetBytes(_username + "|" + _password)).ToString();

            return p;
        }

        /// <summary>
        /// Query all data in a view with paging
        /// </summary>
        /// <param name="viewname"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public Result<object> Query(string viewname, int start, int count)
        {
            return Query(viewname, "", start, count);
        }

        ///// <summary>
        ///// Query all data associated with the Documnet Type or the View Type with paging
        ///// </summary>
        ///// <param name="view"></param>
        ///// <param name="start"></param>
        ///// <param name="count"></param>
        ///// <returns></returns>
        //public Result<object> Query(Type view, int start, int count)
        //{
        //    Packet p = CreatePacket();
        //    p.Command = "querytype";
        //    p.Start = start;
        //    p.Count = count;
        //    p.Data = new object[] { view.AssemblyQualifiedName, "" };
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (Result<object>)ret.Data;
        //}

        /// <summary>
        /// Query a View with a string filter with paging
        /// </summary>
        /// <param name="viewname"></param>
        /// <param name="filter"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public Result<object> Query(string viewname, string filter, int start, int count, string orderby)
        {
            bool b = false;
            // check if return type exists and copy assembly if needed
            if (_assembly.TryGetValue(viewname, out b) == false)
            {
                Packet pp = CreatePacket();
                pp.Command = "checkassembly";
                pp.Viewname = viewname;
                ReturnPacket r = (ReturnPacket)_client.Send(pp);
                string type = r.Error;
                Type t = Type.GetType(type);
                if (t == null)
                {
                    if (r.Data != null)
                    {
                        var a = Assembly.Load((byte[])r.Data);
                        _assembly.Add(viewname, true);
                    }
                }
                else
                    _assembly.Add(viewname, true);
            }
            Packet p = CreatePacket();
            p.Command = "querystr";
            p.Viewname = viewname;
            p.Data = filter;
            p.Start = start;
            p.Count = count;
            p.OrderBy = orderby;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (Result<object>)ret.Data;
            else
            {
                LastErrorMessage = ret.Error;
                return null;
            }
        }

        /// <summary>
        /// Query a View with a LINQ filter with paging
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="viewname"></param>
        /// <param name="filter"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public Result<object> Query<T>(string viewname, Expression<Predicate<T>> filter, int start, int count, string orderby)
        {
            LINQString ls = new LINQString();
            ls.Visit(filter);
            Packet p = CreatePacket();
            p.Command = "querystr";
            p.Viewname = viewname;
            p.Start = start;
            p.Count = count;
            p.Data = ls.sb.ToString();
            p.OrderBy = orderby;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
                return (Result<object>)ret.Data;
            else
                return null;
        }

        ///// <summary>
        ///// Query a View Type with a LINQ filter with paging
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="type"></param>
        ///// <param name="filter"></param>
        ///// <param name="start"></param>
        ///// <param name="count"></param>
        ///// <returns></returns>
        //public Result<object> Query<T>(Type type, Expression<Predicate<T>> filter, int start, int count)
        //{
        //    LINQString ls = new LINQString();
        //    ls.Visit(filter);
        //    Packet p = CreatePacket();
        //    p.Command = "querytype";
        //    p.Start = start;
        //    p.Count = count;
        //    p.Data = new object[] { type.AssemblyQualifiedName, ls.sb.ToString() };
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (Result<object>)ret.Data;
        //}

        ///// <summary>
        ///// Query a View Type with a string filter with paging
        ///// </summary>
        ///// <param name="type"></param>
        ///// <param name="filter"></param>
        ///// <param name="start"></param>
        ///// <param name="count"></param>
        ///// <returns></returns>
        //public Result<object> Query(Type type, string filter, int start, int count)
        //{
        //    Packet p = CreatePacket();
        //    p.Command = "querytype";
        //    p.Start = start;
        //    p.Count = count;
        //    p.Data = new object[] { type.AssemblyQualifiedName, filter };
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (Result<object>)ret.Data;
        //}

        ///// <summary>
        ///// Count rows
        ///// </summary>
        ///// <param name="type"></param>
        ///// <returns></returns>
        //public int Count(Type type)
        //{
        //    return Count(type, "");
        //}

        ///// <summary>
        ///// Count rows with a string filter
        ///// </summary>
        ///// <param name="type"></param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public int Count(Type type, string filter)
        //{
        //    Packet p = CreatePacket();
        //    p.Command = "counttype";
        //    p.Data = new object[] { type.AssemblyQualifiedName, filter };
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (int)ret.Data;
        //}

        ///// <summary>
        ///// Count rows with a LINQ query
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="type"></param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public int Count<T>(Type type, Expression<Predicate<T>> filter)
        //{
        //    LINQString ls = new LINQString();
        //    ls.Visit(filter);
        //    Packet p = CreatePacket();
        //    p.Command = "counttype";
        //    p.Data = new object[] { type.AssemblyQualifiedName, ls.sb.ToString() };
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (int)ret.Data;
        //}

        /// <summary>
        /// Count rows
        /// </summary>
        /// <param name="viewname"></param>
        /// <returns></returns>
        public int Count(string viewname)
        {
            return Count(viewname, "");
        }

        /// <summary>
        /// Count rows with a string filter
        /// </summary>
        /// <param name="viewname"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public int Count(string viewname, string filter)
        {
            LastErrorMessage = String.Empty;

            Packet p = CreatePacket();
            p.Command = "countstr";
            p.Viewname = viewname;
            p.Data = filter;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            LastErrorMessage = _client.LastErrorMessage;

            if (ret == null)
                return 0;

            if (ret.OK)
                return (int) ret.Data;
            else
                return 0;

        }

        ///// <summary>
        ///// Count rows with a LINQ query
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="viewname"></param>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public int Count<T>(string viewname, Expression<Predicate<T>> filter)
        //{
        //    LINQString ls = new LINQString();
        //    ls.Visit(filter);
        //    Packet p = CreatePacket();
        //    p.Command = "countstr";
        //    p.Viewname = viewname;
        //    p.Data = ls.sb.ToString();
        //    ReturnPacket ret = (ReturnPacket)_client.Send(p);
        //    return (int)ret.Data;
        //}

        /// <summary>
        /// Query with LINQ filter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filter"></param>
        /// <returns></returns>
        public Result<T> Query<T>(Expression<Predicate<T>> filter)
        {
            return Query<T>(filter, 0, -1, "");
        }

        /// <summary>
        /// Query with LINQ filter and paging
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filter"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public Result<T> Query<T>(Expression<Predicate<T>> filter, int start, int count, string orderby)
        {
            LINQString ls = new LINQString();
            ls.Visit(filter);
            Packet p = CreatePacket();
            p.Command = "querytype";
            p.Start = start;
            p.Count = count;
            p.OrderBy = orderby;
            p.Data = new object[] { typeof(T).AssemblyQualifiedName, ls.sb.ToString() };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            if (ret.OK)
            {
                Result<object> res = (Result<object>)ret.Data;
                return GenericResult<T>(res);
            }
            else
                return null;
        }

        private static Result<T> GenericResult<T>(Result<object> res)
        {
            // FEATURE : dirty hack here to cleanup
            Result<T> result = new Result<T>();
            if (res != null)
            {
                result.Count = res.Count;
                result.EX = res.EX;
                result.OK = res.OK;
                result.TotalCount = res.TotalCount;
                result.Rows = res.Rows.Cast<T>().ToList<T>();
            }
            return result;
        }

        /// <summary>
        /// Query with string filter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filter"></param>
        /// <returns></returns>
        public Result<T> Query<T>(string filter)
        {
            return Query<T>(filter, 0, -1,"");
        }

        /// <summary>
        /// Query with string filter and paging
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filter"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public Result<T> Query<T>(string filter, int start, int count, string orderby)
        {
            Packet p = CreatePacket();
            p.Command = "querytype";
            p.Start = start;
            p.Count = count;
            p.OrderBy = orderby;
            p.Data = new object[] { typeof(T).AssemblyQualifiedName, filter };
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            Result<object> res = (Result<object>)ret.Data;
            return GenericResult<T>(res);
        }

        /// <summary>
        /// Count with LINQ filter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filter"></param>
        /// <returns></returns>
        public int Count<T>(Expression<Predicate<T>> filter)
        {
            LINQString ls = new LINQString();
            ls.Visit(filter);
            Packet p = CreatePacket();
            p.Command = "gcount";
            p.Viewname = typeof(T).AssemblyQualifiedName;
            p.Data = ls.sb.ToString();
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return 0;

            return (int) ret.Data;
        }

        /// <summary>
        /// Fetch the document change history
        /// </summary>
        /// <param name="docid"></param>
        /// <returns></returns>
        public int[] FetchHistory(Guid docid)
        {
            Packet p = CreatePacket();
            p.Command = "dochistory";
            p.Docid = docid;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            return (int[])ret.Data;
        }

        /// <summary>
        /// Fetch the file change history
        /// </summary>
        /// <param name="fileid"></param>
        /// <returns></returns>
        public int[] FetchBytesHistory(Guid fileid)
        {
            Packet p = CreatePacket();
            p.Command = "filehistory";
            p.Docid = fileid;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            return (int[])ret.Data;
        }

        /// <summary>
        /// Fetch a specific document version
        /// </summary>
        /// <param name="versionNumber"></param>
        /// <returns></returns>
        public object FetchVersion(int versionNumber)
        {
            Packet p = CreatePacket();
            p.Command = "fetchversion";
            p.Data = versionNumber;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            return ret.Data;
        }

        /// <summary>
        /// Fetch a specific file version
        /// </summary>
        /// <param name="versionNumber"></param>
        /// <returns></returns>
        public byte[] FetchBytesVersion(int versionNumber)
        {
            Packet p = CreatePacket();
            p.Command = "fetchfileversion";
            p.Data = versionNumber;
            ReturnPacket ret = (ReturnPacket)_client.Send(p);

            if (ret == null)
                return null;

            return (byte[])ret.Data;
        }

        public Result<object> Query(string viewname, string filter, int start, int count)
        {
            return this.Query(viewname, filter, start, count, "");
        }

        public Result<object> Query<T>(string viewname, Expression<Predicate<T>> filter, int start, int count)
        {
            return this.Query(viewname, filter, start, count, "");
        }

        public Result<T> Query<T>(Expression<Predicate<T>> filter, int start, int count)
        {
            return Query<T>(filter, start, count, "");
        }

        public Result<T> Query<T>(string filter, int start, int count)
        {
            return Query<T>(filter, start, count, "");
        }

    }
}
