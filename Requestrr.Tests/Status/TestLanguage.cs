using System;
using System.IO;
using Newtonsoft.Json;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.Tests.Status
{
    public static class TestLanguage
    {
        public static readonly Language English = JsonConvert.DeserializeObject<Language>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "english.json")));
    }
}
