namespace NothingVpn.Domain.Models;

/// <summary>DNS-режим. В state.json: doh | system.</summary>
public enum DnsMode
{
    Doh,
    System
}
