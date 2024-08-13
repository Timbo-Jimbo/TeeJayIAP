#if NEWTONSOFT_JSON_AVAILABLE
using Newtonsoft.Json;
#endif

using System;
using UnityEngine.Scripting;

namespace TimboJimbo.InAppPurchasing
{
    #if NEWTONSOFT_JSON_AVAILABLE
    [JsonConverter(typeof(InAppPurchaseTokenJsonConverter))]
    #endif
    public struct InAppPurchaseToken
    {
        private string _token;
        public bool IsValid => !string.IsNullOrEmpty(_token);
        
        public InAppPurchaseToken(string token)
        {
            _token = token;
        }
        
        public override string ToString()
        {
            return _token;
        }

        public bool Equals(InAppPurchaseToken other)
        {
            return _token == other._token;
        }

        public override bool Equals(object obj)
        {
            return obj is InAppPurchaseToken other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (_token != null ? _token.GetHashCode() : 0);
        }
        
        public static bool operator ==(InAppPurchaseToken left, InAppPurchaseToken right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(InAppPurchaseToken left, InAppPurchaseToken right)
        {
            return !left.Equals(right);
        }
        
        public static InAppPurchaseToken Invalid => new () { _token = string.Empty };
        public static InAppPurchaseToken Random => new () { _token = Guid.NewGuid().ToString() };
    }
    
#if NEWTONSOFT_JSON_AVAILABLE
    internal class InAppPurchaseTokenJsonConverter : JsonConverter<InAppPurchaseToken>
    {
        private static readonly Type TargetType = typeof(InAppPurchaseToken);
        
        [Preserve]
        public InAppPurchaseTokenJsonConverter() : base()
        {
        }
        
        // public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        // {
        //     writer.WriteValue((value is InAppPurchaseToken) ? value.ToString() : string.Empty);
        // }
        //
        // public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        // {
        //     return InAppPurchaseToken.Create(reader.Value?.ToString() ?? string.Empty);
        // }
        public override void WriteJson(JsonWriter writer, InAppPurchaseToken value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }


        public override InAppPurchaseToken ReadJson(JsonReader reader, Type objectType, InAppPurchaseToken existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return new InAppPurchaseToken(reader.Value?.ToString() ?? string.Empty);
        }
    }
#endif
}