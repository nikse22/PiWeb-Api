using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeiss.IMT.PiWeb.Api.DataService.Rest;

namespace DemoProject
{
    class Configuration
    {

        public static void CheckAttribute(DataServiceRestClient client, Entity entity, ushort key, string description,
            AttributeType type)
        {

            var config = client.GetConfiguration().Result;
            var definition = config.GetDefinition(key);
            if (definition == null)
            {
                client.CreateAttributeDefinition(entity, new AttributeDefinition
                {
                    Key = key,
                    Description = description,
                    Type = type
                });
            }
            else
            {
                if (config.GetTypeForKey(key) != entity || definition.Description != description ||
                    (definition as AttributeDefinition)?.Type != type)
                {

                    // DO NOT DO THIS if PiWeb database is already in use -> may loss of data
                    client.UpdateAttributeDefinitions(entity, new AbstractAttributeDefinition[]
                    {
                            new AttributeDefinition
                            {
                                Key = key,
                                Description = description,
                                Type = type
                            },
                    });
                }
            }


        }

    }
}
