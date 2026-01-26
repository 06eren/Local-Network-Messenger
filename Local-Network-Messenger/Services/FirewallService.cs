using System;
using System.Globalization;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed record FirewallEnsureResult(bool Success, string Message);

    public sealed class FirewallService
    {
        private const int ProtocolTcp = 6;
        private const int ProtocolUdp = 17;
        private const int DirectionIn = 1;
        private const int ActionAllow = 1;
        private const int ProfileDomain = 1;
        private const int ProfilePrivate = 2;
        private const int ProfilePublic = 4;
        private const int ProfileAll = ProfileDomain | ProfilePrivate | ProfilePublic;

        public Task<FirewallEnsureResult> EnsureAsync(CancellationToken cancellationToken)
        {
            return Task.Run(EnsureInternal, cancellationToken);
        }

        private static FirewallEnsureResult EnsureInternal()
        {
            if (!IsAdministrator())
            {
                return new FirewallEnsureResult(false, "Yonetici yetkisi gerekli.");
            }

            try
            {
                var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyType == null)
                {
                    return new FirewallEnsureResult(false, "Firewall servisi bulunamadi.");
                }

                dynamic policy = Activator.CreateInstance(policyType)!;
                dynamic rules = policy.Rules;

                EnsureRule(rules, "LNM UDP Discovery", ProtocolUdp, 32145, "Yerel ag kesfi (UDP)");
                EnsureRule(rules, "LNM TCP Chat", ProtocolTcp, 32146, "Yerel ag mesajlasma (TCP)");

                return new FirewallEnsureResult(true, "Firewall kurallari guncellendi.");
            }
            catch (Exception ex)
            {
                return new FirewallEnsureResult(false, $"Firewall kurallari ayarlanamadi: {ex.Message}");
            }
        }

        private static void EnsureRule(dynamic rules, string name, int protocol, int port, string description)
        {
            var rule = FindRule(rules, name);
            if (rule == null)
            {
                var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
                if (ruleType == null)
                {
                    return;
                }

                rule = Activator.CreateInstance(ruleType)!;
                rule.Name = name;
                rules.Add(rule);
            }

            rule.Description = description;
            rule.Protocol = protocol;
            rule.LocalPorts = port.ToString(CultureInfo.InvariantCulture);
            rule.Direction = DirectionIn;
            rule.Enabled = true;
            rule.Action = ActionAllow;
            rule.Profiles = ProfileAll;
            rule.InterfaceTypes = "All";
        }

        private static dynamic? FindRule(dynamic rules, string name)
        {
            foreach (var entry in rules)
            {
                try
                {
                    var ruleName = entry.Name as string;
                    if (string.Equals(ruleName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
