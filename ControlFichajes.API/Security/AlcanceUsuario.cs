using System.Collections.Concurrent;
using System.Data;
using ControlFichajes.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlFichajes.API.Security;

internal sealed class AlcanceUsuario : IAsyncDisposable
{
    private readonly IDbContextTransaction? _transaccion;
    private readonly IAsyncDisposable? _puerta;
    private bool _confirmado;

    private AlcanceUsuario(IDbContextTransaction? transaccion, IAsyncDisposable? puerta)
    {
        _transaccion = transaccion;
        _puerta = puerta;
    }

    public static async Task<AlcanceUsuario> AbrirAsync(
        AppDbContext context,
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.IsRelational())
        {
            var transaccion = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            if (EsMySql(context))
                await BloquearFilaAsync(context, usuarioId, cancellationToken);

            return new AlcanceUsuario(transaccion, null);
        }

        var puerta = await Puerta.AdquirirAsync(usuarioId);
        return new AlcanceUsuario(null, puerta);
    }

    public async Task ConfirmarAsync(CancellationToken cancellationToken = default)
    {
        if (_transaccion != null)
            await _transaccion.CommitAsync(cancellationToken);

        _confirmado = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaccion != null)
        {
            if (!_confirmado)
                await _transaccion.RollbackAsync();

            await _transaccion.DisposeAsync();
        }

        if (_puerta != null)
            await _puerta.DisposeAsync();
    }

    private static bool EsMySql(AppDbContext context)
    {
        return context.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task BloquearFilaAsync(
        AppDbContext context,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        var transaccion = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("transaccion_ausente");
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaccion.GetDbTransaction();
        command.CommandText = "SELECT `Id` FROM `Usuario` WHERE `Id` = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = usuarioId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private static class Puerta
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> Puertas = new();

        public static async Task<IAsyncDisposable> AdquirirAsync(int usuarioId)
        {
            var puerta = Puertas.GetOrAdd(usuarioId, _ => new SemaphoreSlim(1, 1));
            await puerta.WaitAsync();
            return new Liberacion(puerta);
        }

        private sealed class Liberacion(SemaphoreSlim puerta) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                puerta.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
