using Newtonsoft.Json;

namespace CxSignHelper.Models
{
    internal class LoginObject
    {
        [JsonProperty("mes")]
        public string Msg { get; set; }
        
        [JsonProperty("mes2")]
        public string Msg2 { get; set; }

        [JsonProperty("status")]
        public bool Status { get; set; }
    }

}
