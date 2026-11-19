using KScript.Metadata;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptDoc("Raises an error.", Example = "<throw type=\"Validation\" message=\"Age must be positive, got {$age}\" />")]
    [KScriptElement(KScriptContentKind.Empty)]
    [KScriptAttribute("type", KScriptValueKind.Text, "The error type, checked with $err.type.")]
    [KScriptAttribute("message", KScriptValueKind.Template, "The message. { } works here.")]
    public class @throw : KScriptObject
    {
        public string type { get; set; }

        public @throw(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && (typeof(KScriptException).IsAssignableFrom(t) || typeof(Exception).IsAssignableFrom(t) && t.Namespace == Global.GlobalIdentifiers.EXCEPTION_TYPES) && t.Name == type
                    select t;

            if (q.FirstOrDefault() != null)
            {
                var ex = Activator.CreateInstance(q.FirstOrDefault(), args: new object[] { Contents });

                Out(Contents);
                if (ex != null)
                {
                    HandleException((Exception)ex);
                    return true;
                }
            }


            var qs = from t in typeof(Exception).Assembly.GetTypes() where typeof(Exception).IsAssignableFrom(t) && t.Name == type select t;

            if (qs.FirstOrDefault() != null)
            {
                Exception ex_s = (Exception)Activator.CreateInstance(qs.FirstOrDefault());
                if (ex_s != null)
                {
                    HandleException(ex_s);
                    return true;
                }
            }

            return true;
        }

        public override string UsageInformation() => "Used to throw an exception specified.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
