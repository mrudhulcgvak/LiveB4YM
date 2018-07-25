using System.Runtime.Serialization;
using Tar.Service.Messages;

namespace Better4You.UserManagment.Service.Messages
{
    [DataContract]
    public class SchoolUserFilterRequest:Request
    {
        [DataMember]
        public string FilterString { get; set; }
    }
}