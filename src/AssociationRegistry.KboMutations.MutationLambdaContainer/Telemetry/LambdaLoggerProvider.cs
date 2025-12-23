namespace AssociationRegistry.KboMutations.MutationLambdaContainer.Telemetry;

using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging;

public class LambdaLoggerProvider : ILoggerProvider
{
    private readonly ILambdaLogger _lambdaLogger;

    public LambdaLoggerProvider(ILambdaLogger lambdaLogger)
    {
        _lambdaLogger = lambdaLogger;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LambdaLogger(_lambdaLogger, categoryName);
    }

    public void Dispose()
    {
    }

    private class LambdaLogger : ILogger
    {
        private readonly ILambdaLogger _lambdaLogger;
        private readonly string _categoryName;

        public LambdaLogger(ILambdaLogger lambdaLogger, string categoryName)
        {
            _lambdaLogger = lambdaLogger;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            _lambdaLogger.LogLine($"[{logLevel}] [{_categoryName}] {message}");
        }
    }
}
