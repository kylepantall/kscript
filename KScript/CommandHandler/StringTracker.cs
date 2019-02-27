using System.Collections.Generic;

namespace KScript.CommandHandler
{
    /// <summary>
    /// String tracker is used to track the start and end of a string
    /// </summary>
    class StringTracker
    {
        readonly Dictionary<int, char> dictionary;

        private bool isInString = false;

        public StringTracker() => dictionary = new Dictionary<int, char>();

        /// <summary>
        /// Adds the char to the dictionary if is expected char.
        /// </summary>
        /// <param name="index">Index to store at</param>
        /// <param name="c">Char to check</param>
        public void Track(int index, char c)
        {
            AddChar(index, c);
            isInString = IsStartOfString(index);
        }

        /// <summary>
        /// Determines if currently parsing within a string
        /// </summary>
        public bool IsInString() => isInString;

        /// <summary>
        /// Checks that the given char c is either (,)'
        /// </summary>
        /// <param name="c">Char to check</param>
        /// <returns>If the char is one of the expected chars</returns>
        public bool IsExpectedChar(char c) => c.Equals('(') || c.Equals(',') || c.Equals(')') || c.Equals(char.Parse("'"));

        /// <summary>
        /// If the char is the expected char, add to dictionary. Otherwise, ignore.
        /// </summary>
        /// <param name="i">Index to store at (used as key)</param>
        /// <param name="c">Char to store at key i</param>
        public void AddChar(int i, char c)
        {
            if (IsExpectedChar(c))
            {
                dictionary.Add(i, c);
            }
        }

        /// <summary>
        /// Clears the dictionary.
        /// </summary>
        public void Clear() => dictionary.Clear();


        /// <summary>
        /// Checks that the given char is the end of a string
        /// </summary>
        /// <param name="currentIndex"></param>
        /// <returns></returns>
        public bool IsEndOfString(int currentIndex)
        {
            //If , then check if a key exists of currentIndex - 1. If true, is either ) or ,

            if (dictionary.ContainsKey(currentIndex))
            {
                if (dictionary.ContainsKey(currentIndex - 1))
                {
                    int index = currentIndex - 1;
                    return (dictionary[currentIndex] == ')' || dictionary[currentIndex] == ',') && dictionary[index] == char.Parse("'");
                }
            }
            return false;
        }

        /// <summary>
        /// Checks the given char is the start of a string
        /// </summary>
        /// <param name="currentIndex"></param>
        /// <returns></returns>
        public bool IsStartOfString(int currentIndex)
        {
            //Check there exists a key for the index before currentIndex. (currentIndex - 1)
            //If true, does the char at currentIndex - 1 == ( or , and char at  currentIndex == '

            if (dictionary.ContainsKey(currentIndex - 1))
            {
                if (dictionary.ContainsKey(currentIndex))
                {
                    int index = currentIndex - 1;
                    return (dictionary[index] == '(' || dictionary[index] == ',') && dictionary[currentIndex] == char.Parse("'");
                }
            }
            return false;
        }
    }
}
