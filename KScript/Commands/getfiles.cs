﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using KScript.Handlers;

namespace KScript.Commands
{
    /// <summary>
    /// Command used to retrieve files from a given directory.
    /// </summary>
    public class getfiles : KScriptCommand
    {
        private readonly string directory;
        private readonly string format = "$value";
        private readonly string delimiter = ",";

        /// <summary>
        /// Constructs the getfiles command.
        /// </summary>
        /// <param name="directory">Directory to retrieve files.</param>
        /// <param name="delimiter">Delimiter used to return the array as a single line string.</param>
        /// <param name="format">Format to apply to each individual item, by default: $value.</param>
        public getfiles(string directory, string delimiter = ",", string format = "$value")
        {
            this.directory = directory;
            this.format = format;
            this.delimiter = delimiter;
        }

        /// <summary>
        /// Used to specify a delimiter
        /// </summary>
        /// <param name="directory">Directory to retrieve files.</param>
        /// <param name="delimiter">Delimited used to return the array as a single line string.</param>
        public getfiles(string directory, string delimiter = ",")
        {
            this.directory = directory;
            this.delimiter = delimiter;
        }

        /// <summary>
        /// Used to retrieve files from given directory - using default delimiter and format.
        /// </summary>
        /// <param name="directory">Directory to retrieve files.</param>
        public getfiles(string directory) => this.directory = directory;


        /// <summary>
        /// Returns the array from a directory.
        /// </summary>
        /// <returns></returns>
        public override string Calculate()
        {
            string[] vals = Directory.GetFiles(directory).Select(i => KScriptReplacer.Replace(format, new KeyValuePair<string, string>("value", i))).ToArray();
            string result = string.Join(delimiter, vals);
            return result;
        }
        public override void Validate() { }
    }
}