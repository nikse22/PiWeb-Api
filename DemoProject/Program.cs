using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using Zeiss.IMT.PiWeb.Api.Common.Data;
using Zeiss.IMT.PiWeb.Api.DataService.Rest;
using Zeiss.IMT.PiWeb.Api.RawDataService.Rest;

namespace DemoProject
{
    class Program
    {
        private static Uri _ServerUri = new Uri("http://localhost:8080/");

        static void Main(string[] args)
        {
            using (var client = new DataServiceRestClient(_ServerUri))
            {
                using (var rawClient = new RawDataServiceRestClient(_ServerUri))
                {

                    var charcateristics = new List<Characteristic>
                    {
                        new Characteristic
                        {
                            Name = "Char1",
                            Value = 0.16,
                            Attributes = new Dictionary<string, double>
                            {
                                { "LowerTolerance", -0.2 },
                                { "UpperTolerance", 0.2 },
                                { "NominalValue", 0.0 }
                            }
                        },
                        new Characteristic
                        {
                            Name = "Char2",
                            Value = 0.21,
                            Attributes = new Dictionary<string, double>
                            {
                                { "LowerTolerance", -0.2 },
                                { "UpperTolerance", 0.2 },
                                { "NominalValue", 0.0 }
                            }
                        },
                        new Characteristic
                        {
                            Name = "Char3",
                            Value = 0.22,
                            Attributes = new Dictionary<string, double>
                            {
                                { "LowerTolerance", -0.2 },
                                { "UpperTolerance", 0.2 },
                                { "NominalValue", 0.0 }
                            }
                        }
                    };

                    var mapping = new Dictionary<string, ushort>
                    {
                        { "LowerTolerance", 2110 },
                        { "UpperTolerance", 2111 },
                        { "NominalValue", 2101 }
                    };

                    Import(client, rawClient, "MyInspection", charcateristics, mapping);

                    var parts = SearchForParts(client);

                    var measurements = SearchForMeasurements(client, parts);

                    GetProtocols(rawClient, measurements);
                }
            }
        }

        private static IEnumerable<InspectionPlanPart> SearchForParts(DataServiceRestClient client)
        {
            //Filtering parts by attributes happen on client not on server side!!
            var result = client.GetParts(
                PathInformation.Root,                               // Part to be fetched by its path
                new[] { Guid.NewGuid(), Guid.NewGuid() },               // Parts to be fetched by its uuids
                1,                                                  // Determines how many levels of the inspection plan tree 
                                                                    //hierarchy should be fetched. Setting depth=0 means that only the entity itself should be fetched, 
                                                                    //depth=1 means the entity and its direct children should be fetched. Please note that depth is treated relative of the path depth of the provided part.
                new AttributeSelector(AllAttributeSelection.True),  //Restricts the result to certain attributes
                false                                               // Determines whether the version history should be fetched or not. This only effects the query if versioning is activated on the server side. 
            ).Result;

            return result;
        }

        private static void GetProtocols(RawDataServiceRestClient rawClient, SimpleMeasurement[] measurements)
        {
            //1. Fetch information about raw data values that exist for the given measurements
            var informationList = new List<RawDataInformation>();
            foreach (var measurement in measurements)
            {
                var target = RawDataTargetEntity.CreateForMeasurement(measurement.Uuid);
                var rawDataInformation = rawClient.ListRawData(new[] { target }).Result;
                informationList.AddRange(rawDataInformation);
            }

            //2. Fetch all the attached files and write it to the disk
            string tempPath = @"C:\Temp\";
            foreach (var info in informationList)
            {
                var bytes = rawClient.GetRawDataForMeasurement(new Guid(info.Target.Uuid), info.Key).Result;
                System.IO.File.WriteAllBytes(string.Concat(tempPath, info.FileName), bytes);
            }
        }

        private static SimpleMeasurement[] SearchForMeasurements(DataServiceRestClient client, IEnumerable<InspectionPlanPart> parts)
        {
            var result = client.GetMeasurements(
                PathInformation.Root,                                              // Part where to search measurements 
                new MeasurementFilterAttributes
                {
                    AggregationMeasurements = AggregationMeasurementSelection.All, // Decide how to include aggregated measurements in your query
                    Deep = true,                                                   // A deep search will find measurements recursively below the start path
                    FromModificationDate = null,                                   // Will only search measurements with a modification date (LastModified) newer than the specified one
                    ToModificationDate = null,                                     // Will only search measurements with a modification date (LastModified) older than the specified one
                    LimitResult = 10,                                              // Will limit the number of returned measurements
                    MeasurementUuids = null,                                       // Use measurement uuids to search for specific measurements
                    OrderBy = new[]                                                // Order the returned measurements by specific attributes
					{
                        new Order( WellKnownKeys.Measurement.Time, OrderDirection.Asc, Entity.Measurement )
                    },
                    PartUuids = parts.Select(p => p.Uuid).ToArray(),                //Restrict the search to certain parts by its uuids
                    RequestedMeasurementAttributes = null,                         // Specify, which measurement attributes should be returned (default: all)
                    SearchCondition = new GenericSearchAttributeCondition          // You can create more complex attribute conditions using the GenericSearchAnd, GenericSearchOr and GenericSearchNot class
                    {
                        Attribute = WellKnownKeys.Measurement.Time,                //Only measurement attributes are supported
                        Operation = Operation.GreaterThan,
                        Value = XmlConvert.ToString(DateTime.UtcNow - TimeSpan.FromDays(2), XmlDateTimeSerializationMode.Utc)
                    }
                }).Result;

            return result;
        }

        private static void Import(DataServiceRestClient client, RawDataServiceRestClient rawClient, string inspectionName, IEnumerable<Characteristic> data, Dictionary<string, ushort> mapping)
        {
            //1. Check server configuration
            foreach (var entry in mapping)
            {
                Configuration.CheckAttribute(client, Entity.Characteristic, entry.Value, entry.Key, AttributeType.Float);
            }

            //1.1 Make sure that essential attributes Time(4) and Value(1) do exist
            Configuration.CheckAttribute(client, Entity.Measurement, WellKnownKeys.Measurement.Time, "Time", AttributeType.DateTime);
            Configuration.CheckAttribute(client, Entity.Value, WellKnownKeys.Value.MeasuredValue, "Value", AttributeType.Float);

            //2. Check the inspection plan

            //2.1 Check the part
            var part = InspectionPlan.GetOrCreatePart(client, inspectionName);

            //2.2 Check the characteristics
            var characteristicMapping = new Dictionary<Characteristic, InspectionPlanCharacteristic>();

            foreach (var characteristic in data)
            {
                var inspectionPlanCharacteristic = InspectionPlan.GetOrCreateCharacteristic(client, part.Path.Name,
                                                   characteristic.Name, mapping, characteristic.Attributes);
                characteristicMapping.Add(characteristic, inspectionPlanCharacteristic);
            }

            //3. Create measurements
            var datacharcateristics = characteristicMapping.Select(pair => new DataCharacteristic
            {
                Uuid = pair.Value.Uuid,
                Path = pair.Value.Path,
                Value = new DataValue
                {
                    Attributes = new[] {
                        new Zeiss.IMT.PiWeb.Api.DataService.Rest.Attribute( WellKnownKeys.Value.MeasuredValue, pair.Key.Value ) }
                }
            }).ToArray();

            var measurement = new DataMeasurement
            {
                Uuid = Guid.NewGuid(),
                PartUuid = part.Uuid,
                Time = DateTime.UtcNow,
                Attributes = new[]
                {
                    new Zeiss.IMT.PiWeb.Api.DataService.Rest.Attribute( WellKnownKeys.Measurement.Time, DateTime.UtcNow )
                },
                Characteristics = datacharcateristics
            };

            //3.1 Finally write measurement to database
            client.CreateMeasurementValues(new[] { measurement }).Wait();

            //4. Write the pdf file as addtional data to the measurement 
            var byteData = System.IO.File.ReadAllBytes(@"C:\Temp\protocol.pdf");
            var target = RawDataTargetEntity.CreateForMeasurement(measurement.Uuid);

            //Notes:	- see e.g. http://wiki.selfhtml.org/wiki/Referenz:MIME-Typen for a complete list of mime types
            //			- When using Key = -1, the server will generate a new key

            rawClient.CreateRawData(new RawDataInformation
            {
                FileName = "Protocol.pdf",
                MimeType = "application/pdf",
                Key = -1,
                Created = DateTime.Now,
                LastModified = DateTime.Now,
                MD5 = new Guid(MD5.Create().ComputeHash(byteData)),
                Size = byteData.Length,
                Target = target
            }, byteData).Wait();
        }
    }
}
