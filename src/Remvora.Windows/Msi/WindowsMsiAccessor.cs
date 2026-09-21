using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Remvora.Windows.Msi;

/// <summary>
/// Model representing an installed Windows Installer (MSI) product.
/// </summary>
public sealed record DiscoveredMsiProduct(
    string ProductCode,
    string ProductName,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    string? InstallDate,
    bool IsPerMachine);

/// <summary>
/// Abstraction over Windows Installer (MSI) enumeration.
/// </summary>
public interface IMsiAccessor
{
    IReadOnlyList<DiscoveredMsiProduct> GetInstalledProducts();
}

/// <summary>
/// Production MSI accessor querying msi.dll using standard Windows Installer APIs.
/// </summary>
public sealed partial class WindowsMsiAccessor : IMsiAccessor
{
    private const int ProductGuidLength = 39; // 38 chars + null terminator
    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;

    [LibraryImport("msi.dll", EntryPoint = "MsiEnumProductsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiEnumProducts(uint iProductIndex, Span<char> lpProductBuf);

    [LibraryImport("msi.dll", EntryPoint = "MsiGetProductInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiGetProductInfo(
        string szProduct,
        string szAttribute,
        Span<char> lpValueBuf,
        ref uint pcchValueBuf);

    public IReadOnlyList<DiscoveredMsiProduct> GetInstalledProducts()
    {
        var products = new List<DiscoveredMsiProduct>();
        uint index = 0;
        Span<char> productBuf = stackalloc char[ProductGuidLength];

        while (true)
        {
            var res = MsiEnumProducts(index++, productBuf);
            if (res == ErrorNoMoreItems)
                break;

            if (res != ErrorSuccess)
                continue;

            var nullIndex = productBuf.IndexOf('\0');
            var slice = nullIndex >= 0 ? productBuf[..nullIndex] : productBuf;
            var productCode = slice.ToString().Trim();

            if (string.IsNullOrWhiteSpace(productCode))
                continue;

            var name = GetProperty(productCode, "InstalledProductName");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var publisher = GetProperty(productCode, "Publisher");
            var version = GetProperty(productCode, "VersionString");
            var location = GetProperty(productCode, "InstallLocation");
            var installDate = GetProperty(productCode, "InstallDate");
            var assignment = GetProperty(productCode, "AssignmentType");
            var isPerMachine = assignment == "1";

            products.Add(new DiscoveredMsiProduct(
                productCode,
                name,
                publisher,
                version,
                location,
                installDate,
                isPerMachine));
        }

        return products;
    }

    private static string? GetProperty(string productCode, string propertyName)
    {
        uint bufferSize = 512;
        Span<char> buffer = stackalloc char[(int)bufferSize];

        var res = MsiGetProductInfo(productCode, propertyName, buffer, ref bufferSize);
        if (res == ErrorSuccess)
        {
            var value = buffer[..(int)bufferSize].ToString().TrimEnd('\0').Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }
}
