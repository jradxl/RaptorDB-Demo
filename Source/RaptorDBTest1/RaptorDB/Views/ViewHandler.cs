﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.ComponentModel;
using RaptorDB.Common;
using System.Threading;
using fastJSON;

namespace RaptorDB.Views
{
    public class ViewRowDefinition
    {
        public ViewRowDefinition()
        {
            Columns = new List<KeyValuePair<string, Type>>();
        }
        public string Name { get; set; }
        public List<KeyValuePair<string, Type>> Columns { get; set; }

        public void Add(string name, Type type)
        {
            Columns.Add(new KeyValuePair<string, Type>(name, type));
        }
    }

    internal class ViewHandler
    {
        private ILog _log = LogManager.GetLogger(typeof(ViewHandler));

        public ViewHandler(string path, ViewManager manager)
        {
            _Path = path;
            _viewmanager = manager;
            _saveTimer = new System.Timers.Timer();
            _saveTimer.AutoReset = true;
            _saveTimer.Elapsed += new System.Timers.ElapsedEventHandler(_saveTimer_Elapsed);
            _saveTimer.Interval = Global.SaveIndexToDiskTimerSeconds * 1000;
            _saveTimer.Start();
        }


        private string _S = Path.DirectorySeparatorChar.ToString();
        private string _Path = "";
        private ViewManager _viewmanager;
        internal ViewBase _view;
        private SafeDictionary<string, IIndex> _indexes = new SafeDictionary<string, IIndex>();
        private StorageFile<Guid> _viewData;
        private BoolIndex _deletedRows;
        private string _docid = "docid";
        private List<string> _colnames = new List<string>();
        private RowFill _rowfiller;
        private ViewRowDefinition _schema;
        private SafeDictionary<int, Dictionary<Guid, List<object[]>>> _transactions = new SafeDictionary<int, Dictionary<Guid, List<object[]>>>();
        private SafeDictionary<string, int> _nocase = new SafeDictionary<string, int>();
        private System.Timers.Timer _saveTimer;

        void _saveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            foreach (var i in _indexes)
                i.Value.SaveIndex();

            _deletedRows.SaveIndex();
        }

        internal void SetView<T>(View<T> view, IDocStorage<Guid> docs)
        {
            bool rebuild = false;
            _view = view;
            // generate schemacolumns from schema
            GenerateSchemaColumns(_view);

            if (_Path.EndsWith(_S) == false) _Path += _S;
            _Path += view.Name + _S;
            if (Directory.Exists(_Path) == false)
            {
                Directory.CreateDirectory(_Path);
                rebuild = true;
            }
            else
            {
                // read version file and check with view
                int version = 0;
                if (File.Exists(_Path + "version_.dat"))
                {
                    version = Helper.ToInt32(File.ReadAllBytes(_Path + "version_.dat"), 0);
                    if (version < view.Version)
                    {
                        _log.Debug("Newer view version detected");
                        _log.Debug("Deleting view = " + view.Name);
                        Directory.Delete(_Path, true);
                        Directory.CreateDirectory(_Path);
                        rebuild = true;
                    }
                }
            }

            // load indexes here
            CreateLoadIndexes(_schema);

            LoadDeletedRowsBitmap();

            _viewData = new StorageFile<Guid>(_Path + view.Name + ".mgdat", SF_FORMAT.BSON, false);

            CreateResultRowFiller();

            if (rebuild)
                Task.Factory.StartNew(() => RebuildFromScratch(docs));
        }

        internal void FreeMemory()
        {
            _log.Debug("free memory : " + _view.Name);
            foreach (var i in _indexes)
                i.Value.FreeMemory();

            _deletedRows.FreeMemory();
        }

        internal void Commit(int ID)
        {
            Dictionary<Guid, List<object[]>> rows = null;
            // save data to indexes
            if (_transactions.TryGetValue(ID, out rows))
                SaveAndIndex(rows);

            // remove in memory data
            _transactions.Remove(ID);
        }

        internal void RollBack(int ID)
        {
            // remove in memory data
            _transactions.Remove(ID);
        }

        internal void Insert<T>(Guid guid, T doc)
        {
            apimapper api = new apimapper(_viewmanager);
            View<T> view = (View<T>)_view;

            if (view.Mapper != null)
                view.Mapper(api, guid, doc);

            // map objects to rows
            foreach (var d in api.emitobj)
                api.emit.Add(d.Key, ExtractRows(d.Value));

            SaveAndIndex(api.emit);
        }

        private void SaveAndIndex(Dictionary<Guid, List<object[]>> rows)
        {
            foreach (var d in rows)
            {
                // delete any items with docid in view
                if (_view.DeleteBeforeInsert)
                    DeleteRowsWith(d.Key);
                // insert new items into view
                InsertRowsWithIndexUpdate(d.Key, d.Value);
            }
        }

        internal bool InsertTransaction<T>(Guid docid, T doc)
        {
            apimapper api = new apimapper(_viewmanager);
            View<T> view = (View<T>)_view;

            try
            {
                if (view.Mapper != null)
                    view.Mapper(api, docid, doc);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                return false;
            }

            if (api._RollBack == true)
                return false;

            // map emitobj -> rows
            foreach (var d in api.emitobj)
                api.emit.Add(d.Key, ExtractRows(d.Value));

            Dictionary<Guid, List<object[]>> rows = new Dictionary<Guid, List<object[]>>();
            if (_transactions.TryGetValue(Thread.CurrentThread.ManagedThreadId, out rows))
            {
                // TODO : exists -> merge data??
            }
            else
            {
                _transactions.Add(Thread.CurrentThread.ManagedThreadId, api.emit);
            }

            return true;
        }

        // FEATURE : add query caching here
        SafeDictionary<string, LambdaExpression> _lambdacache = new SafeDictionary<string, LambdaExpression>();
        internal Result<object> Query(string filter, int start, int count)
        {
            return Query(filter, start, count, "");
        }

        internal Result<object> Query(string filter, int start, int count, string orderby)
        {
            filter = filter.Trim();
            if (filter == "")
                return Query(start, count, orderby);

            DateTime dt = FastDateTime.Now;
            _log.Debug("query : " + _view.Name);
            _log.Debug("query : " + filter);
            _log.Debug("orderby : " + orderby);

            WAHBitArray ba = new WAHBitArray();
            var delbits = _deletedRows.GetBits();
            if (filter != "")
            {
                LambdaExpression le = null;
                if (_lambdacache.TryGetValue(filter, out le) == false)
                {
                    le = System.Linq.Dynamic.DynamicExpression.ParseLambda(_view.Schema, typeof(bool), filter, null);
                    _lambdacache.Add(filter, le);
                }
                QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
                qv.Visit(le.Body);

                ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);
            }
            else
                ba = ba.Fill(_viewData.Count()).AndNot(delbits);

            var order = SortBy(orderby);

            _log.Debug("query bitmap done (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            dt = FastDateTime.Now;
            // exec query return rows
            return ReturnRows<object>(ba, null, start, count, order);
        }

        internal Result<object> Query<T>(Expression<Predicate<T>> filter, int start, int count)
        {
            return Query<T>(filter, start, count, "");
        }

        // FEATURE : add query caching here
        internal Result<object> Query<T>(Expression<Predicate<T>> filter, int start, int count, string orderby)
        {
            if (filter == null)
                return Query(start, count);

            DateTime dt = FastDateTime.Now;
            _log.Debug("query : " + _view.Name);

            WAHBitArray ba = new WAHBitArray();

            QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
            qv.Visit(filter);
            var delbits = _deletedRows.GetBits();
            ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);
            List<T> trows = null;
            if (_viewmanager.inTransaction())
            {
                // query from transaction own data
                Dictionary<Guid, List<object[]>> rows = null;
                if (_transactions.TryGetValue(Thread.CurrentThread.ManagedThreadId, out rows))
                {
                    List<T> rrows = new List<T>();
                    foreach (var kv in rows)
                    {
                        foreach (var r in kv.Value)
                        {
                            object o = FastCreateObject(_view.Schema);
                            rrows.Add((T)_rowfiller(o, r));
                        }
                    }
                    trows = rrows.FindAll(filter.Compile());
                }
            }

            var order = SortBy(orderby);

            _log.Debug("query bitmap done (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            dt = FastDateTime.Now;
            // exec query return rows
            return ReturnRows<T>(ba, trows, start, count, order);
        }
        internal Result<object> Query(int start, int count)
        {
            return Query(start, count, "");
        }

        internal Result<object> Query(int start, int count, string orderby)
        {
            // no filter query -> just show all the data
            DateTime dt = FastDateTime.Now;
            _log.Debug("query : " + _view.Name);
            int c = _viewData.Count();
            List<object> rows = new List<object>();
            Result<object> ret = new Result<object>();
            int skip = start;
            int cc = 0;
            WAHBitArray del = _deletedRows.GetBits();
            ret.TotalCount = c - (int)del.CountOnes();
            
            var order = SortBy(orderby);

            if (order.Count == 0)
                for (int i = 0; i < c; i++)
                    order.Add(i);

            if (count == -1)
                count = c;

            foreach (int i in order)
            {
                if (del.Get(i) == true)
                    continue;
                if (skip > 0)
                    skip--;
                else
                {
                    bool b = OutputRow<object>(rows, i);
                    if (b && count > 0)
                        cc++;
                }
                if (cc == count) break;
            }

            _log.Debug("query rows fetched (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            _log.Debug("query rows count : " + rows.Count.ToString("#,0"));
            ret.OK = true;
            ret.Count = rows.Count;
            //ret.TotalCount = rows.Count;
            ret.Rows = rows;
            return ret;
        }

        internal void Shutdown()
        {
            _log.Debug("Shutting down Viewhandler");
            // shutdown indexes
            foreach (var v in _indexes)
            {
                _log.Debug("Shutting down view index : " + v.Key);
                v.Value.Shutdown();
            }
            // save deletedbitmap
            _deletedRows.Shutdown();

            _viewData.Shutdown();

            // write view version
            if (File.Exists(_Path + "version_.dat") == false)
                File.WriteAllBytes(_Path + "version_.dat", Helper.GetBytes(_view.Version, false));
        }

        internal void Delete(Guid docid)
        {
            DeleteRowsWith(docid);
        }

        #region [  private methods  ]

        private void CreateResultRowFiller()
        {
            _rowfiller = (RowFill)CreateRowFillerDelegate(_view.Schema);
            // init the row create 
            _createrow = null;
            FastCreateObject(_view.Schema);
        }

        public delegate object RowFill(object o, object[] data);
        public static RowFill CreateRowFillerDelegate(Type objtype)
        {
            DynamicMethod dynMethod = new DynamicMethod("_", typeof(object), new Type[] { typeof(object), typeof(object[]) });
            ILGenerator il = dynMethod.GetILGenerator();
            var local = il.DeclareLocal(objtype);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, objtype);
            il.Emit(OpCodes.Stloc, local);
            int i = 1;

            foreach (var c in objtype.GetFields())
            {
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Ldarg_1);
                if (c.Name != "docid")
                    il.Emit(OpCodes.Ldc_I4, i++);
                else
                    il.Emit(OpCodes.Ldc_I4, 0);

                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Unbox_Any, c.FieldType);
                il.Emit(OpCodes.Stfld, c);
            }

            foreach (var c in objtype.GetProperties())
            {
                MethodInfo setMethod = c.GetSetMethod();
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Ldarg_1);
                if (c.Name != "docid")
                    il.Emit(OpCodes.Ldc_I4, i++);
                else
                    il.Emit(OpCodes.Ldc_I4, 0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Unbox_Any, c.PropertyType);
                il.EmitCall(OpCodes.Callvirt, setMethod, null);
            }

            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Ret);

            return (RowFill)dynMethod.CreateDelegate(typeof(RowFill));
        }

        private Result<object> ReturnRows<T>(WAHBitArray ba, List<T> trows, int start, int count, List<int> orderby)
        {
            DateTime dt = FastDateTime.Now;
            List<object> rows = new List<object>();
            Result<object> ret = new Result<object>();
            int skip = start;
            int c = 0;
            ret.TotalCount = (int)ba.CountOnes();
            if (count == -1) count = ret.TotalCount;
            if (count > 0)
            {
                foreach (int i in orderby)
                {
                    if (ba.Get(i))
                    {
                        if (skip > 0)
                            skip--;
                        else
                        {
                            bool b = OutputRow<object>(rows, i);
                            if (b && count > 0)
                                c++;
                        }
                        ba.Set(i, false);
                        if (c == count) break;
                    }
                }
                foreach (int i in ba.GetBitIndexes())
                {
                    if (c < count)
                    {
                        if (skip > 0)
                            skip--;
                        else
                        {
                            bool b = OutputRow<object>(rows, i);
                            if (b && count > 0)
                                c++;
                        }
                        if (c == count) break;
                    }
                }
            }
            if (trows != null) // FIX : move to start and decrement in count
                foreach (var o in trows)
                    rows.Add(o);
            _log.Debug("query rows fetched (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            _log.Debug("query rows count : " + rows.Count.ToString("#,0"));
            ret.OK = true;
            ret.Count = rows.Count;
            ret.Rows = rows;
            return ret;
        }

        private bool OutputRow<T>(List<T> rows, int i)
        {
            byte[] b = _viewData.ReadRawData(i);
            if (b != null)
            {
                object o = FastCreateObject(_view.Schema);
                object[] data = (object[])fastBinaryJSON.BJSON.Instance.ToObject(b);

                var rf1 = _rowfiller(o, data);
                var rf2 = (T) rf1;

                rows.Add(rf2);

                return true;
            }
            return false;
        }

        private Result<T> ReturnRows2<T>(WAHBitArray ba, List<T> trows, int start, int count, List<int> orderby)
        {
            DateTime dt = FastDateTime.Now;
            List<T> rows = new List<T>();
            Result<T> ret = new Result<T>();
            int skip = start;
            int c = 0;
            ret.TotalCount = (int)ba.CountOnes();
            if (count == -1) count = ret.TotalCount;
            if (count > 0)
            {
                foreach (int i in orderby)
                {
                    if (ba.Get(i))
                    {
                        if (skip > 0)
                            skip--;
                        else
                        {
                            bool b = OutputRow<T>(rows, i);
                            if (b && count > 0)
                                c++;
                        }
                        ba.Set(i, false);
                        if (c == count) break;
                    }
                }
                foreach (int i in ba.GetBitIndexes())
                {
                    if (c < count)
                    {
                        if (skip > 0)
                            skip--;
                        else
                        {
                            bool b = OutputRow<T>(rows, i);
                            if (b && count > 0)
                                c++;
                        }
                        if (c == count) break;
                    }
                }
            }
            if (trows != null)// FIX : move to start and decrement in count
                foreach (var o in trows)
                    rows.Add(o);
            _log.Debug("query rows fetched (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            _log.Debug("query rows count : " + rows.Count.ToString("#,0"));
            ret.OK = true;
            ret.Count = rows.Count;
            ret.Rows = rows;
            return ret;
        }

        private CreateRow _createrow = null;
        private delegate object CreateRow();
        private object FastCreateObject(Type objtype)
        {
            try
            {
                if (_createrow != null)
                    return _createrow();
                else
                {
                    DynamicMethod dynMethod = new DynamicMethod("_", objtype, null);
                    ILGenerator ilGen = dynMethod.GetILGenerator();

                    ilGen.Emit(OpCodes.Newobj, objtype.GetConstructor(Type.EmptyTypes));
                    ilGen.Emit(OpCodes.Ret);
                    _createrow = (CreateRow)dynMethod.CreateDelegate(typeof(CreateRow));
                    return _createrow();
                }
            }
            catch (Exception exc)
            {
                throw new Exception(string.Format("Failed to fast create instance for type '{0}' from assemebly '{1}'",
                    objtype.FullName, objtype.AssemblyQualifiedName), exc);
            }
        }

        MethodInfo view = null;
        private void RebuildFromScratch(IDocStorage<Guid> docs)
        {
            view = this.GetType().GetMethod("Insert", BindingFlags.Instance | BindingFlags.NonPublic);
            _log.Debug("Rebuilding view from scratch...");
            _log.Debug("View = " + _view.Name);
            DateTime dt = FastDateTime.Now;

            int c = docs.RecordCount();
            for (int i = 0; i < c; i++)
            {
                StorageItem<Guid> meta = null;
                object b = docs.GetObject(i, out meta);
                if (meta != null && meta.isDeleted)
                    Delete(meta.key);
                else
                {
                    if (b != null)
                    {
                        // FEATURE : optimize this by not creating the object if not in FireOnTypes
                        object obj = b;
                        Type t = obj.GetType();
                        if (_view.FireOnTypes.Contains(t))//.AssemblyQualifiedName))
                        {
                            var m = view.MakeGenericMethod(new Type[] { obj.GetType() });
                            m.Invoke(this, new object[] { meta.key, obj });
                        }
                    }
                    else
                        _log.Error("Doc is null : " + meta.key);
                }
            }
            _log.Debug("rebuild view '" + _view.Name + "' done (s) = " + FastDateTime.Now.Subtract(dt).TotalSeconds);

            // write version.dat file when done
            File.WriteAllBytes(_Path + "version_.dat", Helper.GetBytes(_view.Version, false));
        }

        private object CreateObject(byte[] b)
        {
            if (b[0] < 32)
                return fastBinaryJSON.BJSON.Instance.ToObject(b);
            else
                return fastJSON.JSON.Instance.ToObject(Encoding.ASCII.GetString(b));
        }

        private void CreateLoadIndexes(ViewRowDefinition viewRowDefinition)
        {
            int i = 0;
            _indexes.Add(_docid, new TypeIndexes<Guid>(_Path, _docid, 16));
            // load indexes
            foreach (var c in viewRowDefinition.Columns)
            {
                if (c.Key != "docid")
                    _indexes.Add(_schema.Columns[i].Key,
                              CreateIndex(
                                _schema.Columns[i].Key,
                                _schema.Columns[i].Value));
                i++;
            }
        }

        private void GenerateSchemaColumns(ViewBase _view)
        {
            // generate schema columns from schema
            _schema = new ViewRowDefinition();
            _schema.Name = _view.Name;

            foreach (var p in _view.Schema.GetProperties())
            {
                Type t = p.PropertyType;
                if (p.GetCustomAttributes(typeof(FullTextAttribute), true).Length > 0)
                    t = typeof(FullTextString);
                if (_view.FullTextColumns.Contains(p.Name) || _view.FullTextColumns.Contains(p.Name.ToLower()))
                    t = typeof(FullTextString);
                _schema.Add(p.Name, t);

                if (p.GetCustomAttributes(typeof(CaseInsensitiveAttribute), true).Length > 0)
                    _nocase.Add(p.Name, 0);
                if (_view.CaseInsensitiveColumns.Contains(p.Name) || _view.CaseInsensitiveColumns.Contains(p.Name.ToLower()))
                    _nocase.Add(p.Name, 0);
            }

            foreach (var f in _view.Schema.GetFields())
            {
                Type t = f.FieldType;
                if (f.GetCustomAttributes(typeof(FullTextAttribute), true).Length > 0)
                    t = typeof(FullTextString);
                if (_view.FullTextColumns.Contains(f.Name) || _view.FullTextColumns.Contains(f.Name.ToLower()))
                    t = typeof(FullTextString);
                _schema.Add(f.Name, t);

                if (f.GetCustomAttributes(typeof(CaseInsensitiveAttribute), true).Length > 0)
                    _nocase.Add(f.Name, 0);
                if (_view.CaseInsensitiveColumns.Contains(f.Name) || _view.CaseInsensitiveColumns.Contains(f.Name.ToLower()))
                    _nocase.Add(f.Name, 0);
            }

            foreach (var s in _schema.Columns)
                _colnames.Add(s.Key);

            // set column index for nocase
            for (int i = 0; i < _colnames.Count; i++)
            {
                int j = 0;
                if (_nocase.TryGetValue(_colnames[i], out j))
                    _nocase[_colnames[i]] = i;
            }
        }

        private void LoadDeletedRowsBitmap()
        {
            _deletedRows = new BoolIndex(_Path, "deleted_.idx");
        }

        private void InsertRowsWithIndexUpdate(Guid guid, List<object[]> rows)
        {
            foreach (var row in rows)
            {
                object[] r = new object[row.Length + 1];
                r[0] = guid;
                Array.Copy(row, 0, r, 1, row.Length);
                byte[] b = fastBinaryJSON.BJSON.Instance.ToBJSON(r);

                int rownum = _viewData.WriteRawData(b);

                // case insensitve columns here
                foreach (var kv in _nocase)
                    row[kv.Value] = ("" + row[kv.Value]).ToLowerInvariant();

                IndexRow(guid, row, rownum);
            }
        }

        private List<object[]> ExtractRows(List<object> rows)
        {
            List<object[]> output = new List<object[]>();
            // reflection match object properties to the schema row

            int colcount = _schema.Columns.Count;

            foreach (var obj in rows)
            {
                object[] r = new object[colcount];
                int i = 0;
                Getters[] getters = Reflection.Instance.GetGetters(obj.GetType(), false);

                foreach (var c in _schema.Columns)
                {
                    foreach (var g in getters)
                    {
                        //var g = getters[ii];
                        if (g.Name == c.Key)
                        {
                            r[i] = g.Getter(obj);
                            break;
                        }
                    }
                    i++;
                }
                output.Add(r);
            }

            return output;
        }


        private void IndexRow(Guid docid, object[] row, int rownum)
        {
            int i = 0;
            _indexes[_docid].Set(docid, rownum);
            // index the row
            foreach (var d in row)
                _indexes[_colnames[i++]].Set(d, rownum);
        }

        private IIndex CreateIndex(string name, Type type)
        {
            if (type == typeof(FullTextString))
                return new FullTextIndex(_Path, name, false);

            else if (type == typeof(string))
                return new TypeIndexes<string>(_Path, name, Global.DefaultStringKeySize);

            else if (type == typeof(bool))
                return new BoolIndex(_Path, name);

            else if (type.IsEnum)
                return (IIndex)Activator.CreateInstance(
                    typeof(EnumIndex<>).MakeGenericType(type),
                    new object[] { _Path, name });

            else
                return (IIndex)Activator.CreateInstance(
                    typeof(TypeIndexes<>).MakeGenericType(type),
                    new object[] { _Path, name, Global.DefaultStringKeySize });
        }

        private void DeleteRowsWith(Guid guid)
        {
            // find bitmap for guid column
            WAHBitArray gc = QueryColumnExpression(_docid, RDBExpression.Equal, guid);
            _deletedRows.InPlaceOR(gc);
        }

        private WAHBitArray QueryColumnExpression(string colname, RDBExpression exp, object from)
        {
            int i = 0;
            if (_nocase.TryGetValue(colname, out i)) // no case query
                return _indexes[colname].Query(exp, ("" + from).ToLowerInvariant(), _viewData.Count());
            else
                return _indexes[colname].Query(exp, from, _viewData.Count());
        }
        #endregion

        internal int Count<T>(Expression<Predicate<T>> filter)
        {
            int totcount = 0;
            DateTime dt = FastDateTime.Now;
            if (filter == null)
                totcount = internalCount();
            else
            {
                WAHBitArray ba = new WAHBitArray();

                QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
                qv.Visit(filter);
                var delbits = _deletedRows.GetBits();
                ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);

                totcount = (int)ba.CountOnes();
            }
            _log.Debug("Count items = " + totcount);
            _log.Debug("Count time (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            return totcount;
        }

        internal int Count(string filter)
        {
            int totcount = 0;
            DateTime dt = FastDateTime.Now;
            filter = filter.Trim();
            if (filter == null || filter == "")
                totcount = internalCount();
            else
            {
                _log.Debug("Count filter : " + filter);
                WAHBitArray ba = new WAHBitArray();

                LambdaExpression le = null;
                if (_lambdacache.TryGetValue(filter, out le) == false)
                {
                    le = System.Linq.Dynamic.DynamicExpression.ParseLambda(_view.Schema, typeof(bool), filter, null);
                    _lambdacache.Add(filter, le);
                }
                QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
                qv.Visit(le.Body);
                var delbits = _deletedRows.GetBits();
                ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);

                totcount = (int)ba.CountOnes();
            }
            _log.Debug("Count items = " + totcount);
            _log.Debug("Count time (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            return totcount;
        }

        private int internalCount()
        {
            int c = _viewData.Count();
            int cc = (int)_deletedRows.GetBits().CountOnes();
            return c - cc;
        }

        internal Result<T> Query2<T>(Expression<Predicate<T>> filter, int start, int count)
        {
            return Query2<T>(filter, start, count, "");
        }

        internal Result<T> Query2<T>(Expression<Predicate<T>> filter, int start, int count, string orderby)
        {
            DateTime dt = FastDateTime.Now;
            _log.Debug("query : " + _view.Name);

            WAHBitArray ba = new WAHBitArray();

            QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
            qv.Visit(filter);
            var delbits = _deletedRows.GetBits();
            ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);
            List<T> trows = null;
            if (_viewmanager.inTransaction())
            {
                // query from transactions own data
                Dictionary<Guid, List<object[]>> rows = null;
                if (_transactions.TryGetValue(Thread.CurrentThread.ManagedThreadId, out rows))
                {
                    List<T> rrows = new List<T>();
                    foreach (var kv in rows)
                    {
                        foreach (var r in kv.Value)
                        {
                            object o = FastCreateObject(_view.Schema);
                            rrows.Add((T)_rowfiller(o, r));
                        }
                    }
                    trows = rrows.FindAll(filter.Compile());
                }
            }
            var order = SortBy(orderby);

            _log.Debug("query bitmap done (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            dt = FastDateTime.Now;
            // exec query return rows
            return ReturnRows2<T>(ba, trows, start, count, order);
        }

        internal Result<T> Query2<T>(string filter, int start, int count)
        {
            return Query2<T>(filter, start, count, "");
        }

        internal Result<T> Query2<T>(string filter, int start, int count, string orderby)
        {
            DateTime dt = FastDateTime.Now;
            _log.Debug("query : " + _view.Name);
            _log.Debug("query : " + filter);
            _log.Debug("order by : " + orderby);

            WAHBitArray ba = new WAHBitArray();
            var delbits = _deletedRows.GetBits();

            if (filter != "")
            {
                LambdaExpression le = null;
                if (_lambdacache.TryGetValue(filter, out le) == false)
                {
                    le = System.Linq.Dynamic.DynamicExpression.ParseLambda(_view.Schema, typeof(bool), filter, null);
                    _lambdacache.Add(filter, le);
                }
                QueryVisitor qv = new QueryVisitor(QueryColumnExpression);
                qv.Visit(le.Body);

                ba = ((WAHBitArray)qv._bitmap.Pop()).AndNot(delbits);
            }
            else
                ba = ba.Fill(_viewData.Count()).AndNot(delbits);

            var order = SortBy(orderby);

            _log.Debug("query bitmap done (ms) : " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            dt = FastDateTime.Now;
            // exec query return rows
            return ReturnRows2<T>(ba, null, start, count, order);
        }

        internal List<int> SortBy(string sort)
        {
            List<int> sortlist = new List<int>();
            if (sort == "")
                return sortlist;
            string col = "";
            foreach (var c in _schema.Columns)
                if (sort.ToLower().Contains(c.Key.ToLower()))
                    col = c.Key;
            if (col == "")
            {
                _log.Debug("sort column not recognized : " + sort);
                return sortlist;
            }

            DateTime dt = FastDateTime.Now;
            bool desc = false;
            if (sort.ToLower().Contains(" desc"))
                desc = true;
            int count = _viewData.Count();
            IIndex idx = _indexes[col];
            object[] keys = idx.GetKeys();
            Array.Sort(keys);

            foreach (var k in keys)
                foreach (var i in idx.Query(RDBExpression.Equal, k, count).GetBitIndexes())
                    sortlist.Add(i);

            if (desc)
                sortlist.Reverse();
            _log.Debug("Sort column = " + col + ", time (ms) = " + FastDateTime.Now.Subtract(dt).TotalMilliseconds);
            return sortlist;
        }

        internal object GetAssembly(out string typename)
        {
            typename = _view.Schema.AssemblyQualifiedName;
            return File.ReadAllBytes(_view.Schema.Assembly.Location);
        }

        public ViewRowDefinition GetSchema()
        {
            return _schema;
        }
    }
}
