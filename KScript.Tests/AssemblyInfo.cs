[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace KScript.Tests
{
    internal static class SuiteSetup
    {
        /// <summary>
        /// Outside CI, kscript test runs only the tests a change can affect, using a graph it keeps in the project.
        /// The suite's tests run kscript test on the same folders again and again and expect every test to run, so the
        /// suite always counts as CI. TestSelectionTests clears it to test picking.
        /// </summary>
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void RunAsCi()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) Environment.SetEnvironmentVariable("CI", "true");
        }
    }
}
