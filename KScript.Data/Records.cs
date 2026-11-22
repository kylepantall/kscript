using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>What KScript.Data keeps with a model record: whether it is in the database, and the values it was read with.</summary>
    internal sealed class RecordState
    {
        public bool Exists { get; set; }

        /// <summary>The stored values as last read or saved, by field. Fields a query didn't select are missing.</summary>
        public Dictionary<string, object> Original { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>Reading, creating, saving and deleting model records.</summary>
    internal static class Records
    {
        public static RecordState State(Record record) => record.Tag as RecordState;

        /// <summary>A row as the query's record: a model record, or a map for a table with no model.</summary>
        public static object Load(Query query, Dictionary<string, object> row, CallContext call)
        {
            ModelInfo model = query.Model;
            if (model == null) return Rows.Map(row);
            var record = new Record(model.Type);
            var state = new RecordState { Exists = true };
            foreach (FieldDefinition f in model.Type.Fields)
            {
                if (f.Computed) continue;
                Column c = model.Column(f.Name);
                if (row.TryGetValue(f.Name, out object raw))
                {
                    object value = Rows.FromDb(raw, c);
                    if (c.Kind == ColumnKind.Json && value is Dictionary<string, object> or List<object>) value = Typed(call, model, f, value);
                    record[f.Name] = value;
                    state.Original[f.Name] = value;
                }
                else
                {
                    record[f.Name] = null;
                }
            }
            Compute(call, record);
            record.Tag = state;
            return record;
        }

        /// <summary>
        /// A stored map or list as its field's type (an Address record, a list of Tags). A value written by something
        /// else that doesn't fit is read as it is.
        /// </summary>
        private static object Typed(CallContext call, ModelInfo model, FieldDefinition f, object value)
        {
            try
            {
                return Shapes.Convert(call.Interpreter, value, f.Type.WithOptional(f.Optional), $"{model.Name}.{f.Name}", model.Type.Closure, model.Type.Module);
            }
            catch (KScriptException)
            {
                return value;
            }
        }

        /// <summary>Works out the computed fields (<c>label => $first + ' ' + $last</c>) from the stored ones.</summary>
        private static void Compute(CallContext call, Record record)
        {
            foreach (FieldDefinition f in record.Type.Fields.Where(f => f.Computed))
            {
                Scope s = record.Type.Closure.Child();
                foreach (KeyValuePair<string, object> kv in record) s.Declare(kv.Key, kv.Value);
                record[f.Name] = call.Interpreter.EvaluateText(f.ValueText, s, record.Type.Module);
            }
        }

        public static KScriptException NotFound(ModelInfo model, string table, object id)
        {
            string what = model?.Name ?? $"row in {table}";
            var ex = new KScriptException(id != null ? $"There is no {what} with {model?.Key ?? "id"} {Values.ToStr(id)}." : $"No {what} matches.", "ModelNotFound");
            ex.Fields["model"] = model?.Name ?? table;
            ex.Fields["id"] = id;
            return ex;
        }

        /// <summary>A value for a model's field, converted to its type (and checked by its rules).</summary>
        public static object Convert(CallContext call, ModelInfo model, string field, object value)
        {
            FieldDefinition f = model.Type.Field(field);
            if (value is BareWord word) value = word.Name;
            if (f == null || value == null) return value;
            if (value is PasswordHash) return value;
            return Shapes.Convert(call.Interpreter, value, f.Type.WithOptional(f.Optional), $"{model.Name}.{field}", model.Type.Closure, model.Type.Module);
        }

        /// <summary>A map without the fields a model guards, so a form can't set them.</summary>
        public static Dictionary<string, object> Fillable(ModelInfo model, Dictionary<string, object> values) =>
            model.Guarded.Count == 0 ? values : values.Where(kv => !model.Guarded.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        /// <summary><c>$User->create({...})</c>: checks the values against the model, saves them and returns the record.</summary>
        public static Record Create(CallContext call, ModelInfo model, Dictionary<string, object> values)
        {
            Dictionary<string, object> fill = Fillable(model, values).ToDictionary(kv => kv.Key, kv => kv.Value is BareWord w ? w.Name : kv.Value, StringComparer.Ordinal);
            var record = (Record)Shapes.MapAs(call.Interpreter, fill, model.Type, $"The {model.Name}");
            Insert(call, model, record);
            return record;
        }

        /// <summary>A record made with <c>$User->from()</c>, or a new one, as a row of the table.</summary>
        private static void Insert(CallContext call, ModelInfo model, Record record)
        {
            Database db = Database.For(call, model);
            DateTime now = Rows.Clock();
            if (model.Timestamps)
            {
                if (record.GetValueOrDefault(ModelInfo.CreatedAt) == null) record[ModelInfo.CreatedAt] = now;
                if (record.GetValueOrDefault(ModelInfo.UpdatedAt) == null) record[ModelInfo.UpdatedAt] = now;
            }
            HashPasswords(model, record);
            var values = new List<(string Name, SqlExpr Value)>();
            foreach (Column c in model.Columns)
            {
                object value = record.GetValueOrDefault(c.Name);
                if (c.AutoIncrement && value == null) continue;
                values.Add((c.Name, new SqlValue(value, c)));
            }
            Column key = model.KeyColumn;
            record[key.Name] = Rows.FromDb(db.Insert(call.Interpreter, model.Table, values, key), key);
            Remember(record);
        }

        /// <summary>Passwords are stored as hashes: a password set on the record becomes its hash before it is saved.</summary>
        private static void HashPasswords(ModelInfo model, Record record)
        {
            foreach (Column c in model.Columns.Where(c => c.Kind == ColumnKind.Password))
            {
                if (record.GetValueOrDefault(c.Name) is PasswordValue p) record[c.Name] = new PasswordHash(p.Hash(p.Algorithm));
            }
        }

        private static void Remember(Record record)
        {
            var state = State(record) ?? new RecordState();
            state.Exists = true;
            state.Original = record.Where(kv => record.Type.Field(kv.Key) is FieldDefinition f && !f.Computed).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            record.Tag = state;
        }

        /// <summary>The fields whose values differ from the stored ones.</summary>
        public static List<string> Dirty(ModelInfo model, Record record)
        {
            RecordState state = State(record);
            var dirty = new List<string>();
            foreach (Column c in model.Columns)
            {
                object now = record.GetValueOrDefault(c.Name);
                if (state == null || !state.Exists)
                {
                    if (now != null) dirty.Add(c.Name);
                    continue;
                }
                if (!state.Original.TryGetValue(c.Name, out object was))
                {
                    if (now != null) dirty.Add(c.Name);
                    continue;
                }
                if (!Same(was, now)) dirty.Add(c.Name);
            }
            return dirty;
        }

        private static bool Same(object a, object b)
        {
            if (a is PasswordValue || b is PasswordValue) return false;
            if (a is PasswordHash || b is PasswordHash) return Equals(a, b);
            return Values.StrictEquals(a, b);
        }

        /// <summary><c>$record->save()</c>: inserts a new record, or writes the fields that changed.</summary>
        public static Record Save(CallContext call, Record record)
        {
            ModelInfo model = ModelInfo.Of(record);
            if (State(record)?.Exists != true)
            {
                Insert(call, model, record);
                return record;
            }
            List<string> dirty = Dirty(model, record);
            if (dirty.Count == 0) return record;
            if (model.Timestamps && !dirty.Contains(ModelInfo.UpdatedAt))
            {
                record[ModelInfo.UpdatedAt] = Rows.Clock();
                dirty.Add(ModelInfo.UpdatedAt);
            }
            HashPasswords(model, record);
            Database db = Database.For(call, model);
            var w = new SqlWriter(db.Dialect).Append("UPDATE ").Name(model.Table).Append(" SET ");
            for (int i = 0; i < dirty.Count; i++)
            {
                if (i > 0) w.Append(", ");
                w.Name(dirty[i]).Append(" = ").Expr(new SqlValue(record.GetValueOrDefault(dirty[i]), model.Column(dirty[i])));
            }
            Column key = model.KeyColumn;
            w.Append(" WHERE ").Expr(new SqlBinary("=", new SqlColumn(key.Name, key), new SqlValue(State(record).Original.GetValueOrDefault(key.Name) ?? record[key.Name], key)));
            db.Execute(call.Interpreter, w.Build(), model.Table);
            Compute(call, record);
            Remember(record);
            return record;
        }

        /// <summary><c>$record->update(name: 'Joan')</c>: sets the fields (checked as they are set) and saves.</summary>
        public static Record Update(CallContext call, Record record, IEnumerable<KeyValuePair<string, object>> values)
        {
            ModelInfo model = ModelInfo.Of(record);
            foreach (KeyValuePair<string, object> v in values)
            {
                if (model.Guarded.Contains(v.Key)) continue;
                if (model.Type.Field(v.Key) is null) model.Need(v.Key, " for ->update()");
                record.Set(call.Interpreter, v.Key, v.Value is BareWord w ? w.Name : v.Value);
            }
            return Save(call, record);
        }

        private static Query ThisRow(CallContext call, ModelInfo model, Record record)
        {
            RecordState state = State(record);
            if (state?.Exists != true) throw new KScriptException($"This {model.Name} hasn't been saved, so it isn't in the database yet. Call ->save() first.", "Value");
            Database db = Database.For(call, model);
            return new Query(db, model.Table, model).WithTrashed("with").ById(state.Original.GetValueOrDefault(model.Key) ?? record[model.Key]);
        }

        /// <summary><c>$record->delete()</c>: soft-deleting models are marked deleted; others are removed.</summary>
        public static bool Delete(CallContext call, Record record, bool force)
        {
            ModelInfo model = ModelInfo.Of(record);
            Query row = ThisRow(call, model, record);
            if (model.SoftDeletes && !force)
            {
                record[ModelInfo.DeletedAt] = Rows.Clock();
                Save(call, record);
                return true;
            }
            bool deleted = row.Delete(call, force: true) > 0;
            State(record).Exists = false;
            return deleted;
        }

        public static Record Restore(CallContext call, Record record)
        {
            ModelInfo model = ModelInfo.Of(record);
            if (!model.SoftDeletes) throw new KScriptException($"->restore() is for models with soft_deletes: true, and {model.Name} doesn't have it.", "Argument");
            ThisRow(call, model, record);
            record[ModelInfo.DeletedAt] = null;
            return Save(call, record);
        }

        /// <summary><c>$record->fresh()</c>: the record as the database has it now, or null if it is gone.</summary>
        public static object Fresh(CallContext call, Record record)
        {
            ModelInfo model = ModelInfo.Of(record);
            return ThisRow(call, model, record).First(call);
        }
    }
}
