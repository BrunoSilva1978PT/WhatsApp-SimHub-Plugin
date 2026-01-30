namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Estados do plugin durante inicialização e execução
    /// </summary>
    public enum PluginState
    {
        /// <summary>
        /// Plugin acabou de iniciar, verificando dependências
        /// </summary>
        Initializing,
        
        /// <summary>
        /// Instalando Node.js, Git ou npm packages
        /// </summary>
        InstallingDependencies,
        
        /// <summary>
        /// Todas dependências instaladas, pronto para conectar
        /// </summary>
        Ready,
        
        /// <summary>
        /// Conectado ao WhatsApp
        /// </summary>
        Connected,
        
        /// <summary>
        /// Erro durante setup
        /// </summary>
        Error
    }
}
