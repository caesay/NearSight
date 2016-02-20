using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CS;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Json;
using RT.Util.Serialization;

namespace NearSight.Util
{
    /// <summary>Offers a convenient way to use <see cref="Classify"/> to serialize objects using the MsgPack format.</summary>
    public static class ClassifyMPack
    {
        public static IClassifyFormat<MPack> DefaultFormat = ClassifyMPackFormat.Default;
        public static T DeserializeFile<T>(string filename, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.DeserializeFile<MPack, T>(filename, format ?? DefaultFormat, options);
        }
        public static object DeserializeFile(Type type, string filename, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.DeserializeFile<MPack>(type, filename, format ?? DefaultFormat, options);
        }
        public static T Deserialize<T>(MPack value, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.Deserialize<MPack, T>(value, format ?? DefaultFormat, options);
        }
        public static object Deserialize(Type type, MPack value, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.Deserialize<MPack>(type, value, format ?? DefaultFormat, options);
        }
        public static void DeserializeIntoObject<T>(MPack value, T intoObject, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            Classify.DeserializeIntoObject<MPack, T>(value, intoObject, format ?? DefaultFormat, options);
        }
        public static void DeserializeFileIntoObject(string filename, object intoObject, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            Classify.DeserializeFileIntoObject<MPack>(filename, intoObject, format ?? DefaultFormat, options);
        }
        public static void SerializeToFile<T>(T saveObject, string filename, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            Classify.SerializeToFile<MPack, T>(saveObject, filename, format ?? DefaultFormat, options);
        }
        public static void SerializeToFile(Type saveType, object saveObject, string filename, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            Classify.SerializeToFile<MPack>(saveType, saveObject, filename, format ?? DefaultFormat, options);
        }
        public static MPack Serialize<T>(T saveObject, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.Serialize<MPack, T>(saveObject, format ?? DefaultFormat, options);
        }
        public static MPack Serialize(Type saveType, object saveObject, ClassifyOptions options = null, IClassifyFormat<MPack> format = null)
        {
            return Classify.Serialize<MPack>(saveType, saveObject, format ?? DefaultFormat, options);
        }
    }
    public interface IClassifyBinaryObjectProcessor : IClassifyObjectProcessor<MPack>
    {
    }

    public interface IClassifyBinaryTypeProcessor : IClassifyTypeProcessor<MPack>
    {
    }
    public sealed class ClassifyMPackFormat : IClassifyFormat<MPack>
    {
        /// <summary>Gets the Classify format with all options at their defaults.</summary>
        public static IClassifyFormat<MPack> Default { get { return _default ?? (_default = new ClassifyMPackFormat()); } }
        private static ClassifyMPackFormat _default;

        private const string META_COLON_ONE = ":";
        private const string META_COLON_TWO = META_COLON_ONE + META_COLON_ONE;
        private const string META_VALUE = META_COLON_ONE + "v";
        private const string META_VALUE_S = META_COLON_ONE + "m";
        private const string META_DECLARING_TYPES = META_COLON_ONE + "d";
        private const string META_FULLTYPE = META_COLON_ONE + "f";
        private const string META_TYPE = META_COLON_ONE + "t";
        private const string META_REF = META_COLON_ONE + "e";
        private const string META_REF_ID = META_COLON_ONE + "r";
        private const string META_ID = META_COLON_ONE + "i";

        private ClassifyMPackFormat() { }

        MPack IClassifyFormat<MPack>.ReadFromStream(Stream stream)
        {
            return MPack.ParseFromStream(stream);
        }

        void IClassifyFormat<MPack>.WriteToStream(MPack element, Stream stream)
        {
            element.EncodeToStream(stream);
        }

        bool IClassifyFormat<MPack>.IsNull(MPack element)
        {
            return element == null || element.ValueType == MPackType.Null;
        }

        object IClassifyFormat<MPack>.GetSimpleValue(MPack element)
        {
            if (element is MPackMap)
            {
                var dict = (MPackMap)element;
                if (dict.ContainsKey(META_VALUE))
                {
                    element = dict[META_VALUE];
                }
            }

            return element.Value;
        }

        MPack IClassifyFormat<MPack>.GetSelfValue(MPack element)
        {
            var dict = (MPackMap)element;
            return dict[META_VALUE];
        }

        IEnumerable<MPack> IClassifyFormat<MPack>.GetList(MPack element, int? tupleSize)
        {
            if (element is MPackMap)
            {
                var dict = (MPackMap)element;
                if (dict.ContainsKey(META_VALUE))
                {
                    return (MPackArray)dict[META_VALUE];
                }
            }
            return (MPackArray)element;
        }

        void IClassifyFormat<MPack>.GetKeyValuePair(MPack element, out MPack key, out MPack value)
        {
            MPackArray array = null;
            if (element is MPackMap)
            {
                var dict = (MPackMap)element;
                if (dict.ContainsKey(META_VALUE))
                {
                    array = (MPackArray)dict[META_VALUE];
                }
            }
            if (array == null)
                array = (MPackArray)element;
            key = array[0];
            value = array[1];
        }

        IEnumerable<KeyValuePair<object, MPack>> IClassifyFormat<MPack>.GetDictionary(MPack element)
        {
            var dict = (MPackMap)element;
            return dict.Where(kvp => !kvp.Key.StartsWith(META_COLON_ONE) || kvp.Key.StartsWith(META_COLON_TWO))
                .Select(kvp => new KeyValuePair<object, MPack>(kvp.Key.StartsWith(META_COLON_ONE) ? kvp.Key.Substring(1) : kvp.Key, kvp.Value));
        }

        bool IClassifyFormat<MPack>.HasField(MPack element, string fieldName, string declaringType)
        {
            if (fieldName.StartsWith(META_COLON_ONE))
                fieldName = META_COLON_ONE + fieldName;
            var dict = element as MPackMap;
            return dict != null
                && dict.ContainsKey(fieldName)
                && (!(dict[fieldName] is MPackMap)
                    || !((MPackMap)dict[fieldName]).ContainsKey(META_DECLARING_TYPES)
                    || ((MPackArray)((MPackMap)dict[fieldName])[META_DECLARING_TYPES]).Contains(MPack.From(declaringType)));
        }

        MPack IClassifyFormat<MPack>.GetField(MPack element, string fieldName, string declaringType)
        {
            if (fieldName.StartsWith(META_COLON_ONE))
                fieldName = META_COLON_ONE + fieldName;
            var consider = ((MPackMap)element)[fieldName];
            var considerDict = consider as MPackMap;
            if (considerDict != null && considerDict.ContainsKey(META_DECLARING_TYPES))
            {
                var values = (MPackArray)considerDict[META_VALUE_S];
                var types = (MPackArray)considerDict[META_DECLARING_TYPES];
                var index = types.IndexOf(MPack.From(declaringType));
                return values[index];
            }
            return consider;
        }

        string IClassifyFormat<MPack>.GetType(MPack element, out bool isFullType)
        {
            if (element is MPackMap)
            {
                var dict = (MPackMap)element;
                if (dict.ContainsKey(META_FULLTYPE))
                {
                    isFullType = true;
                    return dict[META_FULLTYPE].To<string>();
                }
                if (dict.ContainsKey(META_TYPE))
                {
                    isFullType = false;
                    return dict[META_TYPE].To<string>();
                }
            }
            isFullType = false;
            return null;
        }

        bool IClassifyFormat<MPack>.IsReference(MPack element)
        {
            return element is MPackMap && ((MPackMap)element).ContainsKey(META_REF);
        }

        bool IClassifyFormat<MPack>.IsReferable(MPack element)
        {
            return element is MPackMap && ((MPackMap)element).ContainsKey(META_REF_ID);
        }

        //bool IClassifyFormat<MPack>.IsFollowID(MPack element)
        //{
        //    return element is MPackMap && ((MPackMap)element).ContainsKey(META_ID);
        //}

        int IClassifyFormat<MPack>.GetReferenceID(MPack element)
        {
            var dict = (MPackMap)element;
            return
                dict.ContainsKey(META_REF) ? dict[META_REF].To<int>() :
                dict.ContainsKey(META_REF_ID) ? dict[META_REF_ID].To<int>() :
                Ut.Throw<int>(new InvalidOperationException("The Binary Classify format encountered a contractual violation perpetrated by Classify. GetReferenceID() should not be called unless IsReference() or IsReferable() returned true."));
        }

        //string IClassifyFormat<MPack>.GetFollowID(MPack element)
        //{
        //    var dict = (MPackMap)element;
        //    return dict.ContainsKey(META_ID)
        //        ? dict[META_ID].To<string>()
        //        : Ut.Throw<string>(new InvalidOperationException("The Binary Classify format encountered a contractual violation perpetrated by Classify. GetFollowID() should not be called unless IsFollowID() returned true."));
        //}

        MPack IClassifyFormat<MPack>.FormatNullValue()
        {
            return MPack.Null();
        }

        MPack IClassifyFormat<MPack>.FormatSimpleValue(object value)
        {
            if (value == null)
                return MPack.Null();

            if(value is Enum)
                return MPack.From(ExactConvert.ToString(value));

            return MPack.From(value);
        }

        MPack IClassifyFormat<MPack>.FormatSelfValue(MPack value)
        {
            var map = new MPackMap();
            map.Add(META_VALUE, value);
            return map;
        }

        MPack IClassifyFormat<MPack>.FormatList(bool isTuple, IEnumerable<MPack> values)
        {
            return new MPackArray(values);
        }

        MPack IClassifyFormat<MPack>.FormatKeyValuePair(MPack key, MPack value)
        {
            return new MPackArray(new[] { key, value });
        }

        MPack IClassifyFormat<MPack>.FormatDictionary(IEnumerable<KeyValuePair<object, MPack>> values)
        {
            return new MPackMap(values.ToDictionary(kvp => ExactConvert.ToString(kvp.Key)
                       .Apply(key => key.StartsWith(META_COLON_ONE) ? META_COLON_ONE + key : key), kvp => kvp.Value));
        }

        MPack IClassifyFormat<MPack>.FormatObject(IEnumerable<ObjectFieldInfo<MPack>> fields)
        {
            var dictz = from f in fields
                        group f by f.FieldName into gr
                        let key = gr.Key.StartsWith(META_COLON_ONE) ? META_COLON_ONE + gr.Key : gr.Key
                        let value = gr.Skip(1).Any()
                            ? new MPackMap()
                                {
                                   { META_DECLARING_TYPES, new MPackArray(gr.Select(elem => MPack.From(elem.DeclaringType))) },
                                   { META_VALUE_S, new MPackArray(gr.Select(elem => elem.Value)) }
                                }
                            : gr.First().Value
                        select new KeyValuePair<string, MPack>(key, value);

            return new MPackMap(dictz);
        }

        //MPack IClassifyFormat<MPack>.FormatFollowID(string id)
        //{
        //    return new MPackMap() { { META_ID, MPack.FromString(id) } };
        //}

        MPack IClassifyFormat<MPack>.FormatReference(int refId)
        {
            return new MPackMap() { { META_REF, MPack.From(refId) } };
        }

        MPack IClassifyFormat<MPack>.FormatReferable(MPack element, int refId)
        {
            if (!(element is MPackMap))
                return new MPackMap() { { META_REF_ID, MPack.From(refId) }, { META_VALUE, element } };

            ((MPackMap)element)[META_REF_ID] = MPack.From(refId);
            return element;
        }

        MPack IClassifyFormat<MPack>.FormatWithType(MPack element, string type, bool isFullType)
        {
            if (!(element is MPackMap))
                return new MPackMap {
                    { isFullType ? META_FULLTYPE : META_TYPE, MPack.From(type) },
                    { META_VALUE, element }
                };

            ((MPackMap)element)[isFullType ? META_FULLTYPE : META_TYPE] = MPack.From(type);
            return element;
        }

        void IClassifyFormat<MPack>.ThrowMissingReferable(int refID)
        {
            throw new InvalidOperationException(@"An object reference ("":ref"": {0}) was encountered, but no matching object ("":refid"": {0}) was encountered during deserialization. If such an object is present somewhere in the JSON, the relevant object was not deserialized (most likely because a field corresponding to a parent object was removed from its class declaration).".Fmt(refID));
        }
    }
}
