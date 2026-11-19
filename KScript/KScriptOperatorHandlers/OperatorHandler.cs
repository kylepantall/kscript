using System.Collections.Generic;

using KScript.Arguments;
using KScript.Document;
using KScript.KScriptTypes;

namespace KScript.KScriptOperatorHandlers
{
    public abstract class OperatorHandler : KScriptIO
    {
        public OperatorHandler(KScriptContainer container) : base(container) { }
        public abstract void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container);
        public abstract bool CanRun(KScriptObject obj);

        /// <summary>
        /// Runs the child nodes of a block (e.g. an &lt;if&gt;) until a &lt;break&gt; or &lt;continue&gt;
        /// is requested or the script is stopped. The request flags are left set so the enclosing loop can act on them.
        /// </summary>
        internal static void RunBlock(KScriptObject owner, List<IKScriptDocumentNode> nodes, KScriptContainer container)
        {
            foreach (IKScriptDocumentNode node in nodes)
            {
                if (!container.AllowExecution || container.BreakRequested || container.ContinueRequested)
                {
                    return;
                }

                KScriptObject value = node.GetValue();

                if (value is @break)
                {
                    container.StopConditionalLoops();
                    return;
                }

                if (value is @continue)
                {
                    container.ContinueRequested = true;
                    return;
                }

                node.Run(container, null, owner);
            }
        }

        /// <summary>
        /// Runs one iteration of a loop body.
        /// </summary>
        /// <returns>False when the loop should stop (break requested or script stopped).</returns>
        internal static bool RunIteration(KScriptObject owner, List<IKScriptDocumentNode> nodes, KScriptContainer container)
        {
            RunBlock(owner, nodes, container);

            // A continue only skips the rest of the current iteration.
            container.ContinueRequested = false;

            if (container.BreakRequested)
            {
                container.AllowConditionalLoops();
                return false;
            }

            return container.AllowExecution;
        }
    }
}
