using ControlFichajes.API.Models;

namespace ControlFichajes.API.Security;

public static class CorreoCuentaEliminada
{
    public static void Liberar(Usuario ocupante)
    {
        var marca = $"baja.{ocupante.Id}.{Guid.NewGuid():N}@eliminado.local";
        ocupante.Correo = marca.Length <= 100 ? marca : marca[..100];
    }
}
