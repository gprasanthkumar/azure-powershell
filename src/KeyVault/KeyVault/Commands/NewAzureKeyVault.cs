﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using Microsoft.Azure.Commands.KeyVault.Models;
using Microsoft.Azure.Commands.KeyVault.Properties;
using Microsoft.Azure.Commands.ResourceManager.Common.ArgumentCompleters;
using Microsoft.Azure.Management.KeyVault.Models;
using Microsoft.WindowsAzure.Commands.Common.CustomAttributes;
using Microsoft.WindowsAzure.Commands.Utilities.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace Microsoft.Azure.Commands.KeyVault
{
    /// <summary>
    /// Create a new key vault.
    /// </summary>
    [Cmdlet("New", ResourceManager.Common.AzureRMConstants.AzureRMPrefix + "KeyVault", SupportsShouldProcess = true)]
    [OutputType(typeof(PSKeyVault))]
    public class NewAzureKeyVault : KeyVaultManagementCmdletBase
    {
        private const string KeyVaultParameterSet = "KeyVaultParameterSet";
        private const string ManagedHsmParameterSet = "ManagedHsmParameterSet";

        #region Common Parameter Definitions
        /// <summary>
        /// Vault name
        /// </summary>
        [Parameter(Mandatory = true,
            Position = 0,
            ValueFromPipelineByPropertyName = true,
            HelpMessage =
                "Specifies a name of the key vault to create. The name can be any combination of letters, digits, or hyphens. The name must start and end with a letter or digit. The name must be universally unique."
            )]
        [ValidateNotNullOrEmpty]
        [Alias("VaultName")]
        public string Name { get; set; }

        /// <summary>
        /// Resource group name
        /// </summary>
        [Parameter(Mandatory = true,
            Position = 1,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Specifies the name of an existing resource group in which to create the key vault.")]
        [ResourceGroupCompleter]
        [ValidateNotNullOrEmpty()]
        public string ResourceGroupName { get; set; }

        /// <summary>
        /// Location
        /// </summary>
        [Parameter(Mandatory = true,
            Position = 2,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Specifies the Azure region in which to create the key vault. Use the command Get-AzResourceProvider with the ProviderNamespace parameter to see your choices.")]
        [LocationCompleter("Microsoft.KeyVault/vaults")]
        [ValidateNotNullOrEmpty()]
        public string Location { get; set; }

        [Parameter(Mandatory = false,
            // Hide out until available
            ParameterSetName = KeyVaultParameterSet,
            HelpMessage = "If specified, 'soft delete' functionality is disabled for this key vault.")]
        public SwitchParameter DisableSoftDelete { get; set; }

        [Parameter(Mandatory = false,
            // Hide out until available
            ParameterSetName = KeyVaultParameterSet,
            HelpMessage = "If specified, protection against immediate deletion is enabled for this vault; requires soft delete to be enabled as well. Enabling 'purge protection' on a key vault is an irreversible action. Once enabled, it cannot be changed or removed.")]
        public SwitchParameter EnablePurgeProtection { get; set; }

        [Parameter(Mandatory = false,
            // Hide out until available
            ParameterSetName = KeyVaultParameterSet,
            HelpMessage = "Specifies how long deleted resources are retained, and how long until a vault or an object in the deleted state can be purged. The default is " + Constants.DefaultSoftDeleteRetentionDaysString + " days.")]
        [ValidateRange(Constants.MinSoftDeleteRetentionDays, Constants.MaxSoftDeleteRetentionDays)]
        [ValidateNotNullOrEmpty]
        public int SoftDeleteRetentionInDays { get; set; }

        [Parameter(Mandatory = false,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Specifies the SKU of the key vault instance. For information about which features are available for each SKU, see the Azure Key Vault Pricing website (http://go.microsoft.com/fwlink/?linkid=512521).")]
        public string Sku { get; set; }

        [Parameter(Mandatory = false,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "A hash table which represents resource tags.")]
        [Alias(Constants.TagsAlias)]
        public Hashtable Tag { get; set; }

        [Parameter(Mandatory = false,
            // Hide out until available
            ParameterSetName = KeyVaultParameterSet,
            HelpMessage = "Specifies the network rule set of the vault. It governs the accessibility of the key vault from specific network locations. Created by `New-AzKeyVaultNetworkRuleSetObject`.")]
        public PSKeyVaultNetworkRuleSet NetworkRuleSet { get; set; }

        #endregion

        #region Keyvault-specified Parameter Definitions

        [Parameter(Mandatory = false,
            ParameterSetName = KeyVaultParameterSet,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "If specified, enables secrets to be retrieved from this key vault by the Microsoft.Compute resource provider when referenced in resource creation.")]
        public SwitchParameter EnabledForDeployment { get; set; }

        [Parameter(Mandatory = false,
            ParameterSetName = KeyVaultParameterSet,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "If specified, enables secrets to be retrieved from this key vault by Azure Resource Manager when referenced in templates.")]
        public SwitchParameter EnabledForTemplateDeployment { get; set; }

        [Parameter(Mandatory = false,
            ParameterSetName = KeyVaultParameterSet,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "If specified, enables secrets to be retrieved from this key vault by Azure Disk Encryption.")]
        public SwitchParameter EnabledForDiskEncryption { get; set; }

        #endregion

        #region Managed HSM-specified Parameter Definitions

        [Parameter(Mandatory = true,
            ParameterSetName = ManagedHsmParameterSet,
            HelpMessage = "Array of initial administrators object ids for this managed hsm pool.")]
        public string[] Administrator { get; set; }

        [Parameter(Mandatory = true,
            ParameterSetName = ManagedHsmParameterSet,
            HelpMessage = "Specifies the type of this vault as Managed HSM.")]
        public SwitchParameter Hsm { get; set; }

        #endregion

        public override void ExecuteCmdlet()
        {
            if (ShouldProcess(Name, Properties.Resources.CreateKeyVault))
            {
                if (VaultExistsInCurrentSubscription(Name, Hsm.IsPresent))
                {
                    throw new ArgumentException(Resources.VaultAlreadyExists);
                }

                var userObjectId = string.Empty;
                AccessPolicyEntry accessPolicy = null;

                try
                {
                    userObjectId = GetCurrentUsersObjectId();
                }
                catch (Exception ex)
                {
                    // Show the graph exceptions as a warning, but still proceed to create a vault with no access policy
                    // This is to unblock Key Vault in Fairfax as Graph has issues in this environment.
                    WriteWarning(ex.Message);
                }

                if (!string.IsNullOrWhiteSpace(userObjectId))
                {
                    accessPolicy = new AccessPolicyEntry()
                    {
                        TenantId = GetTenantId(),
                        ObjectId = userObjectId,
                        Permissions = new Permissions
                        {
                            Keys = DefaultPermissionsToKeys,
                            Secrets = DefaultPermissionsToSecrets,
                            Certificates = DefaultPermissionsToCertificates,
                            Storage = DefaultPermissionsToStorage
                        }
                    };
                }

                // Set common parameters
                var vaultCreationParameter = new VaultCreationParameters()
                {
                    VaultName = this.Name,
                    ResourceGroupName = this.ResourceGroupName,
                    Location = this.Location,
                    SkuName = this.Sku,
                    EnableSoftDelete = !this.DisableSoftDelete.IsPresent,
                    EnablePurgeProtection = EnablePurgeProtection.IsPresent ? true : (bool?)null, // false is not accepted
                    /*
                     * If soft delete is enabled, but retention days is not specified, use the default value,
                     * else use the vault user provides,
                     * else use null
                     */
                    SoftDeleteRetentionInDays = DisableSoftDelete.IsPresent
                        ? null as int?
                        : (this.IsParameterBound(c => c.SoftDeleteRetentionInDays)
                            ? SoftDeleteRetentionInDays
                            : Constants.DefaultSoftDeleteRetentionDays),

                    TenantId = GetTenantId(),
                    AccessPolicy = accessPolicy,
                    NetworkAcls = new NetworkRuleSet(),     // New key-vault takes in default network rule set
                    Tags = this.Tag
                };

                switch (ParameterSetName)
                {
                    case KeyVaultParameterSet:
                        vaultCreationParameter.EnabledForDeployment = this.EnabledForDeployment.IsPresent;
                        vaultCreationParameter.EnabledForTemplateDeployment = EnabledForTemplateDeployment.IsPresent;
                        vaultCreationParameter.EnabledForDiskEncryption = EnabledForDiskEncryption.IsPresent;
                        vaultCreationParameter.SkuFamilyName = DefaultSkuFamily;
                        this.WriteObject(KeyVaultManagementClient.CreateNewVault(vaultCreationParameter, ActiveDirectoryClient, NetworkRuleSet));
                        break;

                    case ManagedHsmParameterSet:
                        vaultCreationParameter.Administrator = this.Administrator;
                        vaultCreationParameter.SkuFamilyName = DefaultManagedHsmSkuFamily;
                        this.WriteObject(KeyVaultManagementClient.CreateNewManagedHsm(vaultCreationParameter, ActiveDirectoryClient, NetworkRuleSet));
                        break;
                    default:
                        throw new ArgumentException(Resources.BadParameterSetName);
                }

          

                if (accessPolicy == null)
                {
                    WriteWarning(Resources.VaultNoAccessPolicyWarning);
                }
            }
        }
    }
}
