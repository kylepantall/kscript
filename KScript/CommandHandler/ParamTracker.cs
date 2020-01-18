using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KScript.CommandHandler
{
    public class ParamTracker
    {
        private bool IsCommand = false;

        private bool Ignore = false;


        public bool HasParams => values.Any();
        public string GetIndexPair()
        {
            stringBuilder = new StringBuilder();
            return values.Dequeue();
        }

        private readonly Queue<string> values = new Queue<string>();
        private StringBuilder stringBuilder = new StringBuilder();

        /// <summary>
        /// Tracks the char and keeps record of the parameters for commands.
        /// </summary>
        /// <param name="ch">Char of current Index</param>
        /// <param name="index">Index of the loop</param>
        public void Track(char ch, int index)
        {
            if (ch.Equals(char.Parse("'")))
            {
                Ignore = !Ignore;
            }

            if ((ch.Equals(',') || ch.Equals(')')) && !IsCommand && !Ignore)
            {
                string value = stringBuilder.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Enqueue(value);
                }

                stringBuilder = new StringBuilder();
                Ignore = false;
            }

            if (ch.Equals('@') && !Ignore)
            {
                IsCommand = true;
                return;
            }

            if ((ch.Equals('(') || ch.Equals(')') || ch.Equals(',')) && !Ignore)
            {
                IsCommand = false;
                return;
            }

            if (!IsCommand)
            {
                stringBuilder.Append(ch);
            }
        }
    }
}
