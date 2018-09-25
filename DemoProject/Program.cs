using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeiss.IMT.PiWeb.Api.Common.Data;

namespace DemoProject
{
    class Program
    {

        private static Uri ServerUri = new Uri("http://IV05N001FB.cznet.zeiss.org:8080/");

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
        }
    }




}
