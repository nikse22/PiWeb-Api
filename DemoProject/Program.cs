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

        private static Uri _ServerUri = new Uri("http://localhost:8080/");

        static void Main(string[] args)
        {

            var characteristics = new List<Characteristic>
            {
                new Characteristic
                {
                    Name = "Char1",
                    Value = 0.16,
                    Attributes = new Dictionary<string, double>()
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
                    Attributes = new Dictionary<string, double>()
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
                    Attributes = new Dictionary<string, double>()
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

            // Make sure that essential attributes do exist
            Configuration.CheckAttribute(client, Entity.Measurement, WellKnownKeys.Measurement.Time, "Time", AttributeType.DateTime);
            Configuration.CheckAttribute(client, Entity.Value, WellKnownKeys.Measurement.Time, "Value", AttributeType.Float);


            // 3. check inspection plan
            var part = InspectionPlan.GetOrCreatePart(client, inspectionName);
            var characteristicMapping = new Dictionary<Characteristic, InspectionPlanCharacteristic>();

            foreach (var characteristic in data)
            {
                var inspectionPlanCharacteristic = InspectionPlan.GetOrCreateCharacteristic(client, part.Path.Name,
                    characteristic.Name, mapping, characteristic.Attributes);
                characteristicMapping.Add(characteristic, inspectionPlanCharacteristic);
            }

            // 4. create measurements
            var dataCharacteristics = characteristicMapping.Select(pair => new DataCharacteristic
            {
                Uuid = pair.Value.Uuid,
                Path = pair.Value.Path,
                Value = new DataValue
                {
                    Attributes = new[]
                    {
                        new Zeiss.IMT.PiWeb.Api.DataService.Rest.Attribute(WellKnownKeys.Value.MeasuredValue, pair.Key.Value),
                    }
                }
            }).ToArray();

            var measurement = new DataMeasurement
            {
                Uuid = Guid.NewGuid(),
                PartUuid = part.Uuid,
                Time = DateTime.UtcNow,
                Attributes = new[]
                {
                    new Zeiss.IMT.PiWeb.Api.DataService.Rest.Attribute(WellKnownKeys.Measurement.Time, DateTime.UtcNow)
                },
                Characteristics = dataCharacteristics
            };

            // 4.1. finally we write measurement to database
        }

    }

}
