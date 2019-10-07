﻿using KScript.Handlers;
using KScript.KScriptObjects;
using Newtonsoft.Json.Linq;
using System;

namespace KScript.Arguments.Array
{
    class marray : KScriptObject
    {
        [KScriptProperty("Used as a unique identifier for this array", true)]
        public string id { get; set; }

        public marray(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            //JArray array_obj = Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>(Contents);
            return MArrayHandler.ConvertJSONToMArray(id, Contents, ParentContainer);
        }

        public override string UsageInformation()
        {
            return @"Used to add a new MultiArray for this script with the given unique name";
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
