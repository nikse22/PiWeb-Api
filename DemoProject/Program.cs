using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeiss.IMT.PiWeb.Api.Common.Data;
using Zeiss.IMT.PiWeb.Api.DataService.Rest;

namespace DemoProject
{
    class Program
    {

        private static Uri _ServerUri = new Uri("http://IV05N001FB.cznet.zeiss.org:8080/");

        static void Main(string[] args)
        {

            var characteristics = new List<Characteristic>
            {
                new Characteristic
                {
                    Name = "Char1",
                    Value = 0.16,
                    Attributes =
                    {
                        {"LowerTolerance", -0.2},
                        {"UpperTolerance", -0.2},
                        {"NominalValue", -0.2}
                    }

                },
                new Characteristic
                {
                    Name = "Char2",
                    Value = 0.21,
                    Attributes =
                    {
                        {"LowerTolerance", -0.4},
                        {"UpperTolerance", -0.4},
                        {"NominalValue", -0.4}
                    }

                },
                new Characteristic
                {
                    Name = "Char3",
                    Value = 0.34,
                    Attributes =
                    {
                        {"LowerTolerance", -0.3},
                        {"UpperTolerance", -0.3},
                        {"NominalValue", -0.3}
                    }

                }
            };

            var mapping = new Dictionary<string, ushort>
            {
                {"LowerTolerance", 2110},
                {"UpperTolerance", 2111},
                {"NominalValue", 2101}
            };

            Import("MyInspection", characteristics, mapping);

        }

        private static void Import(string inspectionName, IEnumerable<Characteristic> data,
            Dictionary<string, ushort> mapping)
        {
            // 1. create the client 
            var client = new DataServiceRestClient(_ServerUri);

            // 2. check server configuration
            foreach (var entry in mapping)
            {
                Configuration.CheckAttribute(client, Entity.Characteristic, entry.Value, entry.Key,
                    AttributeType.Float);
            }
        }

    }

}
