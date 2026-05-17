using System;
using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Aimitra.SamplePlugins.Plugins
{
    public sealed class AstrologerPlugin
    {
        [KernelFunction, Description("Returns a astrological reading for the provided name and date of birth.")]
        public string GetAstrologicalReading(string name, string dateOfBirth)
        {
            return $"Hello, {name}. Your astrological reading for {dateOfBirth} is Bright. You are a natural leader and have a strong sense of justice. This function was loaded from a plugin assembly.";
        }

        
        [KernelFunction, Description("Returns dob of the user by asking same detail from user.")]
        public string GetDateOfBirth(string name)
        {
            Console.WriteLine($"Asking user for date of birth details for {name}...");
            string dob = Console.ReadLine();
            return $"DOB of {name} is {dob}.";
        }


    }
}
