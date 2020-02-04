using KScript.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace KScript.Arguments
{
    public class watcher : KScriptObject
    {
        protected List<Watcher.watch> Watches;
        protected List<Watcher.rule> Rules;

        public watcher()
        {
            Rules = new List<Watcher.rule>();
            Watches = new List<Watcher.watch>();
        }

        public void AddWatcher(Watcher.watch watcher) => Watches.Add(watcher);
        public List<Watcher.watch> GetWatches() => Watches;
        public void AddRule(Watcher.rule rule) => Rules.Add(rule);
        public List<Watcher.rule> GetRules() => Rules;


        [KScriptObjects.KScriptProperty("Used to declare how the watcher should function", true)]
        [KScriptObjects.KScriptAcceptedOptions("repeat", "once")]
        public string handle_method { get; set; } = "repeat";

        public override bool Run()
        {

            // foreach (var watch in GetWatches())
            // {
            //     foreach (var rule in GetRules())
            //     {
            //         def _def = Def(watch.@for);

            //         if (_def == null)
            //         {
            //             return false;
            //         }

            //         if (_def.ValueChanged -= ();)
            //         {
            //             return false;
            //         }

                    
            //     }
            // }

            // GetWatches().ForEach(i => Out(i.@for));
            return true;
        }


        public void RunRule(EventArgs e)
        {

        }

        public override string UsageInformation() => @"Used to watch a variable for provided rulesets. For example, when a variable's becomes empty and respond dynamically.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
