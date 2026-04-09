using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public interface IConversionService
    {
        Task ConvertAsync(ConversionJob job, string delimiter, bool hasHeader, CancellationToken cancellationToken);
    }
}
