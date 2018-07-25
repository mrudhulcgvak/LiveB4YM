using System.Configuration;

namespace Tar.Core.LoggingOld.Configuration
{
    public class LoggerConfigurationElementCollection : ConfigurationElementCollectionBase<LoggerConfigurationElement>
    {
        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((LoggerConfigurationElement)element).Name;
        }

        protected override string ElementName
        {
            get
            {
                return "logger";
            }
        }
    }
}