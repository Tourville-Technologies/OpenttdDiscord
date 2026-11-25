using System.Net;
using FluentValidation;
using OpenttdDiscord.Domain.Servers;

namespace OpenttdDiscord.Validation.Ottd
{
    public class OttdServerValidator : AbstractValidator<OttdServer>
    {
        public OttdServerValidator()
        {
            RuleFor(server => server.AdminPort)
                .Must(port => port >= 0 && port <= 65536)
                .WithMessage("Port must be a number between 0 and 65536!");

            RuleFor(server => server.Name)
                .MinimumLength(1)
                .MaximumLength(30)
                .WithMessage("Server name can be maximum 30 characters long!");

            RuleFor(server => server.AdminPortPassword)
                .Must(password => !string.IsNullOrWhiteSpace(password))
                .WithMessage("Password must be provided");

            RuleFor(server => server.Ip)
                .Must(IsValidAddressOrHostName)
                .WithMessage("Address must be a valid IPv4 address, IPv6 address, or DNS hostname");
        }

        private static bool IsValidAddressOrHostName(string address)
        {
            if (string.IsNullOrWhiteSpace(address) || address != address.Trim() || address.Length > 253)
            {
                return false;
            }

            if (IPAddress.TryParse(address, out _))
            {
                return true;
            }

            string[] labels = address.Split('.');
            if (labels.All(label => label.All(char.IsDigit)))
            {
                return false;
            }

            return labels.All(label =>
                label.Length is >= 1 and <= 63 &&
                label[0] != '-' &&
                label[^1] != '-' &&
                label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
        }
    }
}
