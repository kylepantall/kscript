using KScript.Handlers;
using KScript.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static KScript.KScriptObjects.KScriptValidator;
using System.Text.RegularExpressions;

namespace KScript.KScriptObjects
{
    public class KScriptValidator
    {
        private readonly List<KScriptValidationObject> validationObjects;
        private readonly KScriptContainer Container;

        public enum ExpectedInput
        {
            ArrayID,
            DefID,
            Custom,
            FileLocation,
            DirectoryLocation,
            URL,
            Number,
            Text,
            Bool
        }

        public KScriptValidator(KScriptContainer container)
        {
            validationObjects = new List<KScriptValidationObject>();
            Container = container;
        }

        public void AddValidator(KScriptValidationObject @object) => validationObjects.Add(@object);

        public void Validate(KScriptBaseObject caller)
        {
            foreach (KScriptValidationObject kScriptValidationObject in validationObjects)
            {
                try
                {
                    kScriptValidationObject.Validate(Container, caller);
                }
                catch (System.Exception ex)
                {
                    Container.HandleException(caller, ex);
                    throw new KScriptInvalidScriptType(caller);
                }
            }
        }
    }

    public class KScriptValidationObject
    {
        private readonly string property_name;
        private readonly bool can_be_empty = true;
        private readonly ExpectedInput expected_input = ExpectedInput.Custom;
        private readonly string[] accepted_values = null;
        private readonly string regex;

        public KScriptValidationObject(string property_name, bool can_be_empty = false)
        {
            this.property_name = property_name;
            this.can_be_empty = can_be_empty;
        }

        public KScriptValidationObject(string property_name, bool can_be_empty, params string[] accepted_values)
        {
            this.property_name = property_name;
            this.can_be_empty = can_be_empty;
            this.accepted_values = accepted_values;
        }

        public KScriptValidationObject(string property_name, bool can_be_empty, ExpectedInput expected_input)
        {
            this.property_name = property_name;
            this.can_be_empty = can_be_empty;
            this.expected_input = expected_input;
        }

        public KScriptValidationObject(string property_name, bool can_be_empty, string regex)
        {
            this.property_name = property_name;
            this.can_be_empty = can_be_empty;
            this.regex = regex;
        }

        public string GetPropertyValue(KScriptBaseObject caller)
        {
            string value = (string)caller.GetType().GetProperty(property_name, typeof(string)).GetValue(caller);
            return value;
        }

        /// <summary>
        /// Checks if a property is empty or null if not allowed nullable/empty value.
        /// </summary>
        /// <param name="caller">Object calling the method.</param>
        /// <returns>If the value is allowed to be empty. Else checks if value is empty/null and returns false</returns>
        public bool IsAllowedEmpty(KScriptBaseObject caller)
        {
            if (can_be_empty)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(GetPropertyValue(caller)) || GetPropertyValue(caller) == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks the value is any of the accepted values.
        /// </summary>
        /// <param name="caller">Object calling the method.</param>
        /// <returns>If the value is of the accepted values.</returns>
        public bool IsAcceptedValue(KScriptBaseObject caller)
        {
            if (accepted_values != null)
            {
                return accepted_values.Any(i => GetPropertyValue(caller).Contains(i));
            }

            return true;
        }

        /// <summary>
        /// Checks that the property value returns the expected regular expression.
        /// </summary>
        /// <param name="caller">Object calling method</param>
        /// <returns>If the check was successfull</returns>
        public bool IsExpectedRegularExpression(KScriptBaseObject caller)
        {
            if (string.IsNullOrEmpty(regex))
            {
                return true;
            }

            return new Regex(regex).IsMatch(GetPropertyValue(caller));
        }

        /// <summary>
        /// Checks for existing array or def with specified ID.
        /// If ExpectedInput type is custom, will not be validated. Must be overriden.
        /// </summary>
        /// <param name="container">KScriptContainer object</param>
        /// <param name="caller">Object calling method</param>
        /// <returns>If the check was successfull</returns>
        public bool IsExpectedInput(KScriptContainer container, KScriptBaseObject caller)
        {
            ExpectedInput x = expected_input;
            switch (x)
            {
                case ExpectedInput.ArrayID:
                    string array_id = GetPropertyValue(caller);
                    return container.ArraysGet().ContainsKey(array_id);
                case ExpectedInput.DefID:
                    if (container.Properties.DynamicDefs)
                        return true;
                    string def_id = GetPropertyValue(caller);
                    return container.GetDefs().ContainsKey(def_id);
                case ExpectedInput.DirectoryLocation:
                    string directory = GetPropertyValue(caller);
                    return Directory.Exists(directory);
                case ExpectedInput.FileLocation:
                    string file = GetPropertyValue(caller);
                    return File.Exists(file);
                case ExpectedInput.URL:
                    string url = GetPropertyValue(caller);
                    return Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out _);
                case ExpectedInput.Number:
                    return int.TryParse(GetPropertyValue(caller), out _);
                case ExpectedInput.Bool:
                    bool isBool = KScriptBoolHandler.IsBool(GetPropertyValue(caller));
                    return isBool;
            }
            return true;
        }

        /// <summary>
        /// Checks all appropriate values and throws errors corresponding to the found validation failures.
        /// </summary>
        /// <param name="container">KScriptContainer object</param>
        /// <param name="caller">Object calling the validation.</param>
        public void Validate(KScriptContainer container, KScriptBaseObject caller)
        {
            if (!IsAllowedEmpty(caller))
            {
                throw new KScriptMissingAttribute(caller, string.Format("The KScript Object property '{0}' must be declared and given a value.", GetPropertyValue(caller)));
            }

            if (!IsExpectedInput(container, caller) && !string.IsNullOrEmpty(GetPropertyValue(caller)))
            {
                switch (expected_input)
                {
                    case ExpectedInput.ArrayID:
                        throw new KScriptArrayNotFound(caller, string.Format("The Array '{0}' does not exist.", GetPropertyValue(caller)));
                    case ExpectedInput.DefID:
                        throw new KScriptDefNotFound(caller, string.Format("The def '{0}' does not exist.", GetPropertyValue(caller)));
                    case ExpectedInput.FileLocation:
                        throw new KScriptFileNotFound(caller, string.Format("The file '{0}' does not exist.", GetPropertyValue(caller)));
                    case ExpectedInput.DirectoryLocation:
                        throw new KScriptDirectoryNotFound(caller, string.Format("The directory '{0}' does not exist.", GetPropertyValue(caller)));
                    case ExpectedInput.Number:
                        throw new KScriptException(caller, "Expected input was not of the type 'Number'");
                    case ExpectedInput.Text:
                        throw new KScriptException(caller, "Expected input was not of the type 'Text'");
                    case ExpectedInput.Bool:
                        throw new KScriptException(caller, "Expected value was not of the type 'Bool'");
                    case ExpectedInput.URL:
                        throw new KScriptException(caller, "Expected value was not of the type 'URL'");
                    default:
                        break;
                }
            }

            if (!IsAcceptedValue(caller))
            {
                throw new KScriptException(caller, string.Format("The value '{0}' was not an accepted value for the property '{1}'.", GetPropertyValue(caller), property_name));
            }

            if (!IsExpectedRegularExpression(caller))
            {
                throw new KScriptException(caller, string.Format("The value '{0}' was not an accepted value for the property '{1}'", GetPropertyValue(caller), property_name));
            }

        }
    }
}