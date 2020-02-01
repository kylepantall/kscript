﻿using System;
using System.Collections.Generic;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class def : KScriptObject
    {
        private string contents;

        [KScriptProperty("The unique id for this definition. Must not contain any spaces or $ symbols.", true)]
        [KScriptExample("<def id=\"tmp_str\"> ... </def>")]
        [KScriptExample("<def id=\"username\"> ... </def>")]
        [KScriptExample("<def id=\"email_address\"> ... </def>")]
        public string id { get; set; }

        public HashDictionary<string, object> StateLog = new KScript.HashDictionary<string, object>();
        public new string Contents
        {
            get => contents;
            set
            {
                contents = value;

                this.StateLog.IfNotContain(value, (self) =>
                {
                    var date = DateTime.Now;
                    self.Insert(value, new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0));
                    return;
                });
            }
        }

        public def(string Contents) => this.Contents = Contents;
        public def() => Contents = NULL;
        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(Contents))
            {
                throw new KScriptNoRunMethodImplemented(this);
            }

            ParentContainer[id].Contents = HandleCommands(Contents);
            return true;
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("id", false, Global.GlobalIdentifiers.VARIABLE_NAME_DETECTION));
            validator.Validate(this);
        }

        public override string UsageInformation() => @"Used to declare a variable within the KScript Definition container." +
            "\nThe id attribute is used within other KScriptObjects to retrieve the value of the declared definition." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of the def with the id 'tmp_name'.";
    }
}
