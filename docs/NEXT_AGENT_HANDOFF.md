# Next Agent Handoff

Date: 2026-09-24, updated 2026-09-30

Purpose: orientar uma nova IA/novo agente a continuar o desenvolvimento do Civil3D Hybrid MCP sem depender do historico desta conversa.

## Primeiro, Leia

Leia estes arquivos antes de alterar codigo:

1. `AGENTS.md`
2. `README.md`
3. `docs/ARCHITECTURE.md`
4. `docs/SECURITY_MODEL.md`
5. `docs/SURVEY_MODULE.md`
6. `docs/TOOL_CATALOG.md`
7. `docs/ROADMAP_PROGRESS.md`
8. `docs/FEATURE_MATRIX.md`
9. `docs/PHASE_3C_LIVE_TEST.md`
10. `docs/PHASE_2F_LIVE_TEST.md`
11. `docs/PHASE_1C_LIVE_TEST.md`
12. `docs/PHASE_2E_LIVE_TEST.md`
13. `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`
14. `docs/CONSOLIDATION_REVIEW.md`
15. `docs/DATA_CARRIERS.md`
16. `CHANGELOG.md`

Se for trabalhar em uma tool especifica, leia tambem os testes correspondentes antes de editar a implementacao.

Se for trabalhar na futura trilha de correcao de superficies/TIN, leia tambem:

17. `docs/superpowers/specs/2026-09-30-surface-tin-correction-tools-design.md`
18. `docs/superpowers/plans/2026-09-30-surface-tin-correction-tools.md`

## Estado Atual do Projeto

Status atual: 49 tools, todas validadas ao vivo contra Civil 3D 2026. Ultima fase fechada: **2J** (leituras de bloco), em 2026-09-24.

**ATENCAO - runtime:** desde o update do Civil 3D 2026 de 2026-09-22 o host Autodesk roda em **.NET 10**. `Bridge`, `Tools.AutoCAD`, `Tools.Civil` e seus testes compilam em `net10.0-windows`; o resto segue em .NET 8. Se o build voltar a falhar com CS1705 citando `System.Runtime` mais novo, e o mesmo fenomeno: ver `docs/decisions/ADR-0009-net10-autodesk-host.md`.

Backlog transversal medido em `docs/CONSOLIDATION_REVIEW.md` (defeitos D1-D5 e decisoes R1-R4). Distincao geometrico/exportado em `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`.

Progresso - **revisado em 2026-09-24**, fonte unica em `docs/ROADMAP_PROGRESS.md`. O antigo "Plataforma geral: 87%" nao saia da propria tabela de fases (ponderada da 63%). Ele coincide exatamente com os criterios de sucesso do nucleo (AGENTS.md secao 38, 14 de 16). Rotulo errado, numero certo. Agora sao tres numeros:

- **Nucleo, secao 38: 87%** (14/16; falta C# dinamico; rollback ao vivo e fluxo Survey parciais)
- **Ferramentas do MVP, secoes 14-15: 72%** (34/47; leituras de bloco entregues, escritas de bloco nao)
- **Plataforma inteira, fases ponderadas: 55%** (53% antes da Phase 2J; 63% na tabela antiga com os mesmos pesos)

Por fase, declarado -> revisado: Auditoria 100->95, Core 99->92, Leitura 97->77->88 (Phase 2J), Escrita segura 70->52, Survey 50 (PARADO), Developer Mode 5, Rodovia 28->11 (reescopo de 12 para 9 tools, maior parte e reclassificacao), Hardening 17->20.

Testes: 293 aprovados, 0 falhas, na solucao completa (medido em 2026-09-24). Percentuais conferidos contra `docs/ROADMAP_PROGRESS.md`.

Ultima fase concluida: **Phase 2J**, leituras de bloco. As fases recentes (2J, 6B, 6A, 3E, 2H, 1F, 1E, 1D, 3D, 2G) tem secao propria `## Estado da Phase X` mais abaixo. O encadeamento a seguir e historico mais antigo, comecando na Phase 3C.

- `propertyset_numeric_test_create`
- Classe: WRITE, Risco: Low, Modulo: Civil3D
- Cria uma definicao de teste com prefixo obrigatorio `MCP_NUMERIC_TEST`
- Cria um `DBPoint` temporario e anexa nele um Property Set com uma propriedade `Real`
- Escreve `numeric_value` com `PropertySet.SetAt` e le de volta com `PropertySet.GetAt`
- Retorna `definition_handle`, `object_handle`, `property_set` e metadados de transacao
- Usa `Document.LockDocument()`, transaction AutoCAD, commit no sucesso e rollback antes do commit em erro
- Nao edita objetos de producao existentes, nao remove sets, nao faz batch e nao substitui `propertyset_set`

Fase anterior: Phase 2F, leitura de AEC Property Sets.

- `propertyset_definition_list`, `propertyset_list`, `propertyset_get`
- Classe: READ, Risco: Low, Modulo: Civil3D
- **Primeiras tools fora da API do Civil 3D.** Property Set e recurso AEC, entao Tools.Civil agora referencia `AecPropDataMgd`
- Definicoes vem do dicionario de property set definitions; sets anexados vem de `PropertyDataServices.GetPropertySets`
- Valores voltam como `value` string invariante mais `numeric_value` separado, para calcular sem reparsear
- Handle inexistente retorna `ObjectNotFound`; set nao anexado retorna `PropertySetNotFound`
- Objeto que existe mas nao tem dados retorna `count: 0`, que e resposta valida, nao erro
- As tools de leitura nunca chamam `AddPropertySet`, `RemovePropertySet` ou `SetAt`. Escrita geral de Property Set ainda nao existe; apenas a tool controlada de teste numerico da Phase 3C escreve.

Fase anterior: Phase 1C, monitor de dialogos modais.

- `list_dialogs`
- Classe: READ, Risco: Low, Modulo: Diagnostics
- **Nao passa pelo dispatcher do Autodesk.** E a unica tool assim, e isso e proposital: ela existe para diagnosticar exatamente o caso em que a thread do host esta travada
- Regra de deteccao: modal desabilita a janela dona, entao janela principal desabilitada mais janela filha habilitada significa host bloqueado
- Titulos lidos com `SendMessageTimeout` e `SMTO_ABORTIFHUNG`, nunca `GetWindowText`
- `civil3d_status` agora reporta estado real de dialogo e a flag `DialogMonitor` passou de `partial` para `active`
- Logica de classificacao separada do Win32 atras de `IWindowEnumerator`, coberta por 9 testes

Fase anterior: Phase 2E, leitura de corredores do Civil 3D.

- `corridor_list` e `corridor_get`
- Classe: READ, Risco: Low, Modulo: Civil3D
- `corridor_list` devolve resumo com `baseline_count` e `corridor_surface_count`, sem baselines
- `corridor_get` percorre baselines e regioes, resolvendo nome de alinhamento, perfil e assembly
- Estacao inicial/final da baseline e derivada das regioes
- Nome inexistente retorna `CorridorNotFound`
- `Corridor.Rebuild()` nunca e chamado; rebuild continua nao implementado por ser write longo

Fase anterior: Phase 2D, leitura de perfis do Civil 3D.

- `profile_list`, `profile_get`, `profile_elevation_at_station`
- Classe: READ
- Risco: Low
- Modulo: Civil3D
- API .NET do Civil 3D, sem COM
- Perfis sao enumerados via `Alignment.GetProfileIds()`, entao todo registro carrega `alignment_name`
- Nome de perfil repetido entre alinhamentos retorna `ProfileAmbiguous`, nunca escolhe sozinho
- Nome inexistente retorna `ProfileNotFound`
- `profile_elevation_at_station` retorna elevacao e tambem grade quando legivel
- Nao cria, edita nem apaga perfis

Fase anterior:

- `survey_network_list`
- Classe: READ
- Risco: Low
- Modulo: Survey
- Argumento opcional `project_name`
- Usa Survey COM somente para leitura da colecao de networks
- Requer documento Civil 3D ativo
- Sem documento ativo retorna `DocumentNotFound`
- `project_name` sem correspondencia retorna `SurveyProjectNotFound`
- Nao abre Survey Database
- Nao abre projeto Survey
- Nunca chama `IAeccSurveyNetworks.Create` nem `IAeccSurveyNetworks.Remove`
- Nao muta DWG

## Evidencia de Validacao Mais Recente

Ao final da Phase 3C:

- `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --no-restore --nologo` -> 40 testes aprovados, 0 falhas
- `dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug -v:q --no-restore --nologo` -> 34 testes aprovados, 0 falhas
- `dotnet test .\Civil3D.HybridMcp.sln -c Debug -v:q --no-restore --nologo` -> 161 testes aprovados, 0 falhas
- `dotnet build .\src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj -c Release --no-restore -v:q --nologo` -> 0 erros, 9 warnings MSB3277 Autodesk ja conhecidos
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release` -> bundle instalado
- bundle diagnostic -> `success: true`

Live smoke Civil 3D 2026, sobre COPIA do DWG de teste:

- 37 tools anunciadas
- instancia live: `aebe84c2232e41a0a4e1c65af733d28a`
- desenho de teste: `artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_propertyset_numeric_20260914-111406.dwg`
- `propertyset_numeric_test_create` criou `MCP_NUMERIC_TEST_LIVE_111759`
- `definition_handle`: `16FD0C`
- `object_handle`: `16FD0D`
- property set handle: `16FD10`
- propriedade `NumericValue`, `data_type: Real`, `value: 987.654321`, `numeric_value: 987.654321`
- transaction: `document_locked: true`, `transaction_started: true`, `committed: true`, `rolled_back: false`
- `propertyset_get` no `object_handle` criado leu o mesmo `numeric_value`
- o primeiro retry travou porque o Civil 3D estava bloqueado pelo modal `References - Not Found Files`; a caixa foi lida antes de dispensar com `Ignore unresolved reference files` no DWG de teste
- a instancia de teste foi fechada sem salvar; nao ficou `acad.exe` rodando

## Valores De Property Set

O primeiro DWG de teste nao tinha nenhum set anexado. O usuario entao forneceu `Way-153_25-179.dwg`, que tem, e isso fechou a leitura de valores textuais reais.

Validado ao vivo com dado real:

- enumeracao de `PropertySet.PropertySetData` com valores reais
- resolucao de nome via `PropertyIdToName`
- `propertyset_get` por nome exato e em minusculas
- `PropertySetNotFound`
- filtro `name_contains` sem correspondencia retornando sucesso com `count: 0`

Evidencia: 526 objetos com o set `Dados_Rodovia`, e objetos consecutivos retornaram marcos 529, 530 e 531 da BR-153, provando leitura por objeto e nao cache do primeiro.

O ramo `numeric_value` foi fechado na Phase 3C sem depender de um desenho externo: a tool `propertyset_numeric_test_create` criou uma definicao `Real`, escreveu `987.654321` e `propertyset_get` leu o mesmo valor no desenho de teste temporario.

Detalhes em `docs/PHASE_2F_LIVE_TEST.md` e `docs/PHASE_3C_LIVE_TEST.md`.

## Diagnostico De Host Travado (resolvido na Phase 1C)

Se uma tool travar sem retorno, **chame `list_dialogs` primeiro**. Ela responde mesmo com o host bloqueado.

Sintoma classico: `tools/list` responde na hora, mas `document_list` e qualquer leitura de desenho ficam penduradas. Isso quase sempre e modal, nao bug.

Depois de detectar um modal, leia o conteudo da caixa de dialogo antes de dispensar. Na Phase 3C, o timeout de handshake foi causado por `References - Not Found Files`; a janela foi enumerada, seus botoes foram lidos, e so entao foi usado `Ignore unresolved reference files` porque o alvo era uma copia de DWG de teste.

O que ainda **nao** existe: fechar o dialogo programaticamente. `dismiss_dialog` e `cancel_active_command` continuam nao implementados de proposito. Quando forem feitos, precisam ser WRITE, exigir o titulo esperado como argumento e recusar qualquer dialogo fora de uma allowlist revisada, conforme `AGENTS.md` secao 14. Enquanto isso, dispensar modal e tarefa manual do operador.

Dialogos ja observados neste projeto: `References - Not Found Files` e `Drawing Recovery`, ambos informativos.

## BLOQUEIO: Modulo Survey Parado

Em 2026-09-13 o usuario confirmou que **nao existe projeto Survey nesta maquina**. Uma verificacao de filesystem encontrou apenas um projeto de Data Shortcuts chamado `Teste` no working folder e nenhum arquivo de Survey Database sob as arvores de documentos.

Consequencia:

- `survey_network_list` continua validado ao vivo somente nos caminhos sem documento, sem projetos e projeto inexistente;
- a enumeracao real de itens de network nunca rodou ao vivo;
- a Phase 4E-D (`survey_network_get`) foi iniciada e depois revertida por decisao do usuario, e **nao restou codigo de detalhe de network no repositorio**.

Nao retome trabalho Survey COM mais profundo enquanto nao houver dados Survey reais em uma maquina de validacao. Se o usuario disponibilizar um ambiente com Survey Project e networks, o primeiro passo e repetir o live test da Phase 4E-C.

## Avisos Operacionais Importantes

O diretorio atual nao e um repositorio Git. Comandos como `git status` retornam:

```text
fatal: not a git repository (or any of the parent directories): .git
```

Nao tente criar commits como parte do fluxo normal enquanto isso nao for resolvido.

O usuario concedeu permissao temporaria, **valida apenas para 2026-09-14**, para encerrar `acad.exe` de teste quando o `Quit()` nao bastar, limitada a instancia iniciada pelo proprio agente. Se a data atual nao for 2026-09-14, **pergunte de novo antes de encerrar qualquer processo**.

Historico: permissao equivalente foi concedida em 2026-09-13. O padrao e pedir por sessao.

Antes de instalar bundle:

1. verifique se ha `acad.exe` rodando;
2. se houver, prefira pedir ao usuario para fechar;
3. se a permissao temporaria ainda estiver valida, encerre apenas a instancia necessaria e documente isso;
4. nunca encerre uma instancia com desenho de producao sem confirmar escopo.

Nao execute testes destrutivos em DWG real. Use somente DWG de teste confirmado pelo usuario.

Observacao repetida na Phase 4E-C: o Civil 3D aceita `Quit()` mas pode permanecer vivo depois. Foi necessario encerrar o PID de teste.

## Erro Ja Observado e Como Evitar

Erro modal do AutoCAD:

```text
Cannot find the specified drawing file.
Please verify that the file exists.
```

Causa observada: quoting incorreto ao iniciar AutoCAD/Civil 3D com argumentos `/ld`, `/p`, `/product`, `/language`.

Forma segura ja usada:

```powershell
$acadPath = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe"
$argsText = '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US'
Start-Process -FilePath $acadPath -ArgumentList $argsText -PassThru
```

Evite montar argumentos com quoting quebrado ou passar o caminho do DBX como se fosse DWG.

## Modulos e Arquivos Relevantes

Protocolos Survey:

- `src/Civil3D.HybridMcp.Protocol/SurveyContracts.cs`
- `src/Civil3D.HybridMcp.Protocol/Contracts.cs` (enum `ErrorCode`)

Catalogo e servico Survey:

- `src/Civil3D.HybridMcp.Tools.Survey/SurveyToolCatalog.cs`
- `src/Civil3D.HybridMcp.Tools.Survey/SurveyGeometryService.cs`

Bridge Autodesk:

- `src/Civil3D.HybridMcp.Bridge/BridgeApplication.cs`

Fake bridge e integracao MCP:

- `tests/Civil3D.HybridMcp.Integration.Tests/FakeBridgeHost.cs`
- `tests/Civil3D.HybridMcp.Integration.Tests/ServerFakeBridgeTests.cs`

Modulo Civil 3D (fase mais recente):

- `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs` (servico puro, `ICivilReadAdapter`, validacao e roteamento)
- `src/Civil3D.HybridMcp.Tools.Civil/CivilWritePipeline.cs` (document lock, transaction, commit/rollback)
- `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs` (API .NET do Civil 3D)
- `src/Civil3D.HybridMcp.Tools.Civil/AutodeskPropertySetWriteAdapter.cs` (AEC Property Set numeric write probe)
- `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`

Testes:

- `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`
- `tests/Civil3D.HybridMcp.Tools.Survey.Tests/SurveyToolTests.cs`

Docs a manter em sincronia:

- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY_MODEL.md`
- `docs/SURVEY_MODULE.md`
- `docs/TOOL_CATALOG.md`
- `docs/ROADMAP_PROGRESS.md`
- `docs/FEATURE_MATRIX.md`
- `CHANGELOG.md`

## Padrao Tecnico Atual Para Survey COM

Survey COM so deve ser acessado no Autodesk host context.

O caminho atual e:

```text
MCP client
  -> Server
  -> Named Pipe JSON-RPC
  -> Bridge
  -> SurveyToolHandler
  -> AutodeskIdleDispatcher
  -> SurveyGeometryService
  -> ISurveyDatabaseGateway
  -> AutodeskSurveyComGateway
```

Antes de escrever qualquer adapter COM novo, confirme a superficie real da API. Metodo usado na Phase 4E-C:

```powershell
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom("C:\Program Files\Autodesk\AutoCAD 2026\C3D\Autodesk.AECC.Interop.Survey.dll")
$asm.GetType("Autodesk.AECC.Interop.Survey.IAeccSurveyNetwork").GetProperties() | ForEach-Object { $_.Name }
```

Isso evita inventar membros de API.

Para novas tools Survey COM, mantenha:

- classe READ enquanto possivel;
- erro estruturado, nunca texto solto;
- `DocumentNotFound` quando nao houver documento ativo;
- detalhes no erro quando relevante:
  - `adapter`
  - `document_count`
  - `requires_active_document`
- leitura defensiva de campos opcionais, retornando null quando o COM falhar;
- falha visivel, nunca sucesso simulado, quando um alvo especifico nao pode ser lido;
- sem chamada de open/create/import/write no Survey Database ate uma fase explicitamente planejada;
- live smoke apenas em desenho temporario ou DWG de teste confirmado.

## Estado da Phase 2G

Phase 2G foi implementada e **validada ao vivo**, dentro do relatorio `docs/PHASE_1F_LIVE_TEST.md` (linha de tabela `| 2G | object_get, object_properties | Pass |`). Por isso nao existe `docs/PHASE_2G_LIVE_TEST.md` separado - nao e lacuna.

- `object_get` e `object_properties`, ambas READ/Low, modulo AutoCAD, catalogo de 37 para 39 tools
- localizador e somente `handle`; nome seria ambiguo entre classes e essas tools existem justamente para classes sem lookup tipado
- identidade vem de `RXClass`, entao classe Civil 3D ou AEC reporta pela mesma forma sem o modulo AutoCAD referenciar essas APIs
- `object_properties` enumera por reflexao sobre o tipo concreto, nao por lista fixa
- objeto aberto `ForRead`: o proprio AutoCAD recusa getter que tente mutar, essa e a garantia externa
- valor sempre em `CultureInfo.InvariantCulture`; a maquina de validacao roda pt-BR e o `ToString` padrao emitiria virgula decimal
- getter que lanca vira linha com `error` preenchido, nunca linha omitida nem valor inventado
- teto de 500 propriedades com flag `truncated`
- o modulo AutoCAD agora roteia por nome de tool, porque tem dois handlers

Verificado: 34 testes do modulo, 37 de integracao, 177 na solucao completa, build Release do Bridge com 0 erros, bundle empacotado em `artifacts\Civil3D.HybridMcp.bundle` com diagnostico offline `success: true`, sem issues nem warnings.

**Falta para fechar a fase:** instalar o bundle e rodar live smoke sobre COPIA de DWG de teste confirmado, depois criar `docs/PHASE_2G_LIVE_TEST.md`. O bundle nao foi instalado porque havia um `acad.exe` em execucao que o agente nao iniciou (PID 45368, iniciado 11:31 de 2026-09-14), e substituir o bundle sobrescreveria DLLs que esse processo pode ter carregado.

## Estado da Phase 3D

Phase 3D e uma passada de regressao de rollback sobre os dois write pipelines. Esta completa offline.

- `CivilWritePipeline`, por onde passa a escrita de property set da Phase 3C, nao tinha nenhum teste; agora tem seis
- dois defeitos reais foram encontrados e corrigidos:
  - rollback que falhava **descartava a causa original**; agora o codigo devolvido e `RollbackFailed` com a excecao original como `InnerException`
  - `Dispose` dos escopos chamava `Rollback()` e deixava lancar, **substituindo a excecao que ja estava propagando**; agora `Dispose` nunca lanca
- `LayerToolException` e `CivilToolException` ganharam construtor com `innerException`
- os fakes de escopo foram alinhados aos escopos reais, incluindo o guard de conclusao unica e o rollback no dispose; o fake antigo ignorava os dois, que e exatamente por que a suite passava com o escopo real errado

Casos cobertos nos dois pipelines: commit, rollback, rollback depois de commit, dispose sem commit, rollback que falha, e dispose que nao lanca.

Falta: exercitar uma escrita que falha de verdade contra host ao vivo, quando houver um disponivel.

## Estado da Phase 1D

Phase 1D le o conteudo de um dialogo bloqueante. Completa offline, sem live smoke.

- `IWindowEnumerator` ganhou `EnumerateChildControls`; novo record puro `ControlSnapshot`
- `DialogWindowInfo` ganhou `text`, `buttons` e `content_read`; novo record `DialogButtonInfo` com legenda, `control_id` e estado habilitado
- conteudo e lido **somente** para janelas ja classificadas como dialogo; a janela principal nunca e percorrida, porque ela tem controles demais e essa tool roda com o host travado
- sweep de um dialogo para em 128 controles
- legendas continuam em `SendMessageTimeout` com `SMTO_ABORTIFHUNG`, a mesma regra que a Phase 1C fixou para titulos
- botao e label sao identificados por class name do Win32; edit, list view e o resto sao ignorados em vez de virarem texto que o chamador nao consegue usar
- `content_read: false` distingue dialogo ilegivel de dialogo sem botao, e isso tambem vira `limitation`
- a limitation antiga que dizia que conteudo e legenda nao eram lidos foi removida, e a capability `dialog_content_read` foi adicionada

Isso existe como **pre-requisito de `dismiss_dialog`**, nao como fim. Nao da para desenhar allowlist contra legenda que ninguem consegue ler.

O que falta decidir antes de `dismiss_dialog`, e e decisao do usuario, nao default: quais dialogos e quais botoes podem ser fechados por programa. O `References - Not Found Files` com `Ignore unresolved reference files` e o candidato obvio, ja que bloqueou validacao em 3 fases, mas a allowlist precisa de assinatura explicita.

## Estado da Phase 1E - Registro de Decisoes de Dialogo

Duas tools novas, catalogo de 39 para 41. Completa offline, sem live smoke.

- `dialog_decision_list` (READ/Low) e `dialog_decision_set` (WRITE/Low)
- as duas sao **host-free**, como `list_dialogs`: existem exatamente para o caso em que o dispatcher esta travado pelo dialogo que se quer decidir
- `dialog_decision_set` escreve configuracao do operador, nunca o desenho; por isso nao tem rollback semantics nem transacao

Regra central: uma decisao liga **um titulo exato** a **um botao exato**. Nada de match aproximado.

Escopo e escolha do operador, nao heuristica:

- `Default` vale para as proximas ocorrencias e e persistido em `%LOCALAPPDATA%\Civil3DHybridMcp\dialog-decisions.json`
- `Once` vale uma vez, e gasto no uso e **nunca** vai para disco; uma resposta de uso unico que sobrevivesse a um restart deixaria de ser de uso unico

Quando a plataforma se recusa a agir, e devolve as opcoes:

- `DecisionRequired` - nao ha decisao, ou a de uso unico ja foi gasta
- `ButtonNotPresent` - ha decisao, mas o botao nomeado sumiu ou esta desabilitado; **nunca** cai para outro botao
- `ContentUnreadable` - o conteudo do dialogo nao pode ser lido, entao os botoes sao desconhecidos, nao ausentes

Arquivo ilegivel ou ausente e lido como "nenhum default", que e o estado seguro: o operador e perguntado em vez de a plataforma agir sobre lixo.

## Estado da Phase 1F - `dismiss_dialog`

Existe agora, catalogo de 41 para 42 tools. Completa offline, sem live smoke.

- WRITE, risco Medium, rollback `NotSupported`: um botao apertado nao se desaperta, e a plataforma nao sabe o que aquele botao faz
- host-free, como as outras tools de dialogo
- `expected_title` e **obrigatorio**: o chamador declara qual dialogo quer fechar, em vez de fechar o que estiver na tela
- titulo que nao casa com nada falha `DialogNotFound`; casa com mais de um falha `DialogAmbiguous`; nunca adivinha
- sem decisao registrada, **nada e apertado** e as opcoes voltam com a razao
- `button_text` + `scope` opcionais permitem decidir e aplicar na mesma chamada; a decisao so e gravada depois de o dialogo ser confirmado como existente
- os controles sao relidos imediatamente antes de apertar, entao botao que sumiu entre a leitura e a acao e reportado, nao apertado por handle velho
- clique por `PostMessage`, nao `SendMessage`: a thread principal do host esta travada pelo proprio modal que se quer fechar, e um envio sincrono poderia parar o chamador atras desse mesmo bloqueio
- clique que o host nao aceitou vira `DialogDismissFailed`, nunca sucesso presumido
- apertar fica atras de `IDialogActuator`, separado de `IWindowEnumerator`, entao o caminho de leitura continua provadamente somente leitura

**Live smoke feito em 2026-09-17.** O ciclo completo rodou contra Civil 3D 2026 e passou. Ver `docs/PHASE_1F_LIVE_TEST.md`.

Dois defeitos so apareceram ao vivo, ambos corrigidos:

- os botoes do `References - Not Found Files` reportam `control_id: 0`, entao `WM_COMMAND` por id nao distinguia um do outro; agora o clique e `BM_CLICK` no handle do botao
- `PostMessage` retorna sucesso so por enfileirar; a tool reportava `pressed: true` para clique que o dialogo ignorou. Agora `IDialogActuator.WaitUntilClosed` confirma que o dialogo sumiu, e dialogo ainda aberto vira `DialogDismissFailed`

Legendas reais lidas ao vivo, para quem for preencher a lista de decisoes:

| Dialogo | Botoes |
| --- | --- |
| `References - Not Found Files` | `Open the External References palette`, `Ignore unresolved reference files` |
| `Drawing Recovery` | `&Fechar` |

Atencao ao `&`: e o marcador de acelerador do Windows, e faz parte da legenda como ela e lida. A decisao casa exatamente, entao tem de ser `&Fechar`, nao `Fechar`. Remover o marcador em silencio quebraria a exatidao em que todo o desenho se apoia.

Nenhuma decisao `Default` foi gravada: as duas do teste foram `Once` e o arquivo terminou `[]`.

Dialogos observados neste projeto ate agora, e o que se sabe deles:

| Dialogo | Fases em que bloqueou | Legenda de botao conhecida |
| --- | --- | --- |
| `References - Not Found Files` | 2E, 3B, 3C, 2G | `Ignore unresolved reference files`, registrada na Phase 3C a partir da acao manual do operador, **nao** lida por maquina |
| `Drawing Recovery` | 3B | nenhuma lida ainda |

As legendas reais so serao conhecidas quando `dialog_decision_list` rodar contra um host com o dialogo aberto. Nao preencha a lista por suposicao.

## Estado da Phase 3E - `propertyset_set`

Primeira tool da plataforma que **exige confirmacao**. Catalogo de 42 para 43. Verde offline, **sem live smoke**.

Toda escrita anterior era limitada a algo que a propria plataforma acabara de criar: uma camada nova, um `DBPoint` temporario. Esta altera dado que um engenheiro colocou no desenho.

Fluxo:

1. chamada sem token -> **nada e escrito**, falha `ConfirmationRequired` e a mensagem **e o preview**: nomeia desenho, objeto, set e cada par antes/depois
2. confirma aquele preview exato e chama de novo com o token

O token e ligado a tool, instancia, documento e ao hash do corpo da requisicao. O token viaja no envelope, nao nos argumentos, entao **um token nunca faz parte daquilo que ele confirma**. Confirmar uma mudanca nao autoriza outra.

Isto deu ao modelo de confirmacao do Core o seu **primeiro consumidor**: ele existia desde a Phase 1, com testes, e nenhuma tool o exigia.

Recusas deliberadas:

- nao anexa set (`AddPropertySet` nunca e chamado)
- nao cria propriedade
- recusa propriedade read-only
- recusa requisicao que nao mudaria nada, porque aprovar no-op ensina o operador a aprovar sem ler
- recusa a mesma propriedade citada duas vezes
- `affected_count` conta so o que de fato muda
- re-resolve objeto e set imediatamente antes de escrever; o que mudou entre preview e escrita falha em vez de ser escrito as cegas
- valor em texto invariante e convertido para o tipo AEC declarado antes de escrever; passar string para propriedade `Real` e como um numero vira texto no desenho

**Live smoke feito em 2026-09-19.** Ver `docs/PHASE_3E_6A_LIVE_TEST.md`.

Um defeito so apareceu ao vivo: o token de confirmacao era criado no bridge e **nunca voltava para o chamador**. A tool dizia "confirme" e nao dava com o que confirmar. Corrigido: `ConfirmationEvaluation` devolve o token emitido, `CivilToolException` carrega `Details`, e o erro `ConfirmationRequired` leva preview, `affected_count` e token.

Provado ao vivo em dado real da BR-153: `Dados_Rodovia.Secao` de `529` para `529-TESTE-MCP`, com preview exato, `committed: true` e releitura confirmando. O guard de no-op tambem disparou: repetir a escrita ja aplicada devolveu `InvalidRequest` sem pedir confirmacao.

## Estado da Phase 6A - Modulo Rodovia aberto

Novo projeto `src/Civil3D.HybridMcp.Tools.Roadway` e a primeira tool: `roadway_station_offset_batch`. Catalogo de 43 para 44. Verde offline, **sem live smoke**.

Decisao estrutural que vale respeitar nas proximas tools rodoviarias:

- o modulo **nao referencia Autodesk**. Ele define uma porta estreita, `IRoadwayAlignmentAdapter`, e o bridge adapta o `AutodeskCivilReadAdapter` a ela
- a geometria por ponto e **o mesmo codigo** que `alignment_station_offset` usa e que foi validado ao vivo; nao ha segunda implementacao para divergir
- isso realiza o que o `AGENTS.md` secao 16 pede: tools de rodovia usam as tools genericas abaixo delas, sem duplicar logica

Comportamento que define a tool:

- **lote nao e tudo ou nada**: ponto fora do eixo vira linha com `resolved: false` e motivo, e o resto do lote volta normalmente. `failed_count` maior que zero e resultado normal, nao erro
- eixo inexistente falha **uma vez**, antes de tocar em qualquer ponto: e um erro do chamador, nao quarenta pontos ruins
- teto de 2000 pontos por lote, porque isso roda no idle loop do Autodesk e lista sem limite congelaria a aplicacao
- cada linha carrega `index` e as coordenadas originais, entao o chamador casa resultado sem depender de ordem

**Live smoke feito em 2026-09-19.** Lote com um ponto sobre o eixo e um longe: `resolved_count: 1`, `failed_count: 1`, o ponto ruim nao derrubou o outro. Round-trip de estacao fechou na 11a casa decimal, que e a consequencia de reusar a geometria validada em vez de reimplementar.

Infraestrutura tocada ao criar o modulo: solucao, referencia no Bridge, `scripts/check-bundle-install.ps1` (lista de DLLs exigidas) e `PackagingDiagnosticsTests`. O `package.ps1` copia o diretorio inteiro, entao nao precisou mudar. Bundle empacotado e diagnostico passou com o DLL novo dentro.

## Estado da Phase 2H - `propertyset_find`

Fecha a lacuna que o live test da Phase 3E expos: **toda tool de property set exigia handle, e nada na plataforma produzia um**. O handle usado naquele teste veio de um relatorio antigo, nao do sistema.

- READ/Low, catalogo de 44 para 45. Verde offline, **sem live smoke**
- varre model space e paper space, porque anotacao rodoviaria vive nos dois
- teto de 50.000 objetos; roda no idle loop do Autodesk
- entidade ilegivel e pulada, nunca fatal: um objeto ruim em quatro mil nao pode custar os outros

Tres contagens, porque respondem coisas diferentes:

- `count` - o que voltou
- `matched_count` - o que casou antes do limite
- `scanned_count` - quantos objetos a varredura olhou, para o chamador julgar cobertura

`truncated` e verdadeiro se o limite cortou **ou** se a varredura parou antes do fim do desenho. Prefixo nunca se confunde com resposta inteira.

Filtro com nome mas sem `equals` nem `contains` e **recusado**: filtro que nao restringe nada alargaria o resultado em silencio. Objeto que nao carrega a propriedade filtrada **nao casa**, em vez de casar.

Detalhe de C#: o filtro exato chama `EqualsValue` no codigo porque um record nao pode declarar membro `Equals`; no fio continua `equals`.

**Live smoke feito em 2026-09-19.** 526 portadores encontrados no `Way-153`, batendo com o relatorio da Phase 2F por caminho independente. O primeiro resultado foi o handle `10781C` - exatamente o que a Phase 3E teve de tirar de um relatorio antigo porque a plataforma nao sabia produzir. Filtros validados em dado real: `Secao = 530` devolveu um objeto, valor inexistente devolveu `count: 0` com `scanned_count` real, filtro sem criterio deu `InvalidRequest`.

**Resolvido na Phase 2I:** `max_scan` no pedido, default de 250.000. Re-rodado ao vivo, o mesmo desenho varreu **94.824 objetos** e achou os **mesmos 526** portadores. Ou seja, a contagem anterior ja estava certa, mas a plataforma nao tinha como saber: os portadores calharam de estar nos primeiros 50.000. Qualquer numero fixo e alcancavel em desenho grande o bastante, e so quem espera o Civil 3D pode julgar quanto tempo tolera - por isso o teto e default, nao limite.

Historico do achado: o teto de 50.000 e alcancavel. O `Way-153` bateu nele e reportou `truncated: true` corretamente. Ou seja, find sem filtro em desenho grande devolve **prefixo**, nao a resposta inteira. Antes de concluir qualquer coisa de uma contagem, cheque `truncated`. Elevar o teto ou estreitar a busca sao as saidas; nao assuma que 526 e o total.

**Correcao de documentacao:** o catalogo dizia que a varredura cobre model e paper space. Ela percorre **todo BlockTableRecord**, o que e mais amplo. Corrigido.

## Estado da Phase 6B - `roadway_propertyset_write`

Escrita de property set em lote, **atomica**. Catalogo de 45 para 46. Verde offline, **sem live smoke**.

Duas decisoes separam isto do lote de leitura ao lado:

1. **Tudo ou nada.** Ler meia lista de estacas ainda serve; 526 marcos meio-atualizados e estado pior que nenhum, e ninguem descobre olhando qual metade passou. Todo objeto e resolvido e validado **antes** de qualquer escrita; uma falha aborta o lote inteiro.
2. **Handles explicitos, nunca filtro.** O chamador roda `propertyset_find`, olha o resultado, e passa a lista. A plataforma nao consegue expandir consulta em escrita que o operador nao viu.

Preview e **sumarizado**, nao linha a linha: preview de 526 linhas e preview que ninguem le, e isso transfere responsabilidade sem transferir entendimento. Ele lidera com a escala, nomeia set e desenho, e mostra 3 pares antes/depois reais.

Outras recusas: handle duplicado no mesmo lote (a ordem passaria a importar), lote em que nada mudaria, objeto sem o set, propriedade inexistente, propriedade read-only.

Refatoracoes que isto exigiu:

- `IConfirmationGate`, `ConfirmationDecision`, `ConfirmationEvaluation` e `HmacConfirmationGate` sairam de Tools.Civil para **Core**, para o modulo Roadway exigir confirmacao sem referenciar Autodesk
- `AutodeskCivilWriteContext`, `AutodeskCivilWriteScopeFactory` e `AutodeskCivilWriteScope` viraram publicos, para o bridge compor o mesmo pipeline de transacao

**Live smoke feito em 2026-09-19.** Tres objetos atualizados atomicamente apos confirmacao, no `Way-153`.

Dois defeitos so apareceram ao vivo, ambos corrigidos:

1. **Token perdido.** `RoadwayToolException` nao carregava `Details`, entao a confirmacao chegava sem com que confirmar. Exatamente o mesmo defeito da Phase 3E, que eu corrigi la e nao levei para ca. **Licao: ao criar modulo novo com confirmacao, replique o carregamento de `Details` na excecao.**
2. **Segredo HMAC novo a cada chamada.** O handler construia `new HmacConfirmationTokenService()` dentro do metodo, gerando segredo aleatorio por requisicao; o token emitido no preview nunca validava na confirmacao. Agora o servico de assinatura e campo do handler, um por handler. O `CivilToolHandler` ja fazia certo; o Roadway copiou o padrao errado.

**Atomicidade provada ao vivo**, e so um lote consegue provar isso: lote com handle valido seguido de `DEADBEEF` falhou com `ObjectNotFound` e o objeto valido continuou com o valor anterior. Nada foi escrito.

## Estado da Phase 2J - Leituras de bloco (e o runtime .NET 10)

Implementada e **validada ao vivo em 2026-09-24**, nos dois desenhos do par de referencia. Relatorio: `docs/PHASE_2J_LIVE_TEST.md`. Catalogo de 46 para 49. Testes de 264 para 293.

- `block_list`, `block_reference_list`, `block_get_attributes`, todas READ/Low, modulo AutoCAD, handler proprio `AutoCadBlockToolHandler`
- regras puras em `BlockService`, atras da porta `IBlockReadAdapter`; unico codigo Autodesk em `AutodeskBlockAdapter`, so `ForRead`
- **bloco dinamico e maioria, nao excecao**: 66 de 104 insercoes no geometrico, 1.372 de 1.961 no exportado. A insercao aponta para definicao anonima (`*U12`); o filtro casa o nome efetivo via `DynamicBlockTableRecord`. Sem isso a busca devolveria lista vazia com cara de resposta correta
- **atributo constante** nao existe na insercao, so na definicao; e anexado com `constant: true` e `handle` nulo. A regra vive em `BlockService.MergeConstantAttributes`, usada tanto por `block_get_attributes` quanto por `block_reference_list` com `include_attributes`. Coberta por teste; **nao rodou ao vivo** porque nenhum dos dois desenhos tem atributo constante
- varredura filtra por `ObjectId.ObjectClass` antes de abrir: 38.179 entidades em 68 ms no exportado
- lista cortada diz por que: `truncated_by` = `scan_budget` ou `result_limit`; os dois dispararam ao vivo
- todo campo de nome composto tem `[JsonPropertyName]` explicito, para nao somar ao D2

**Achado que nao estava no escopo e mudou o build: runtime .NET 10.** O primeiro build falhou antes do codigo novo. Um update do Civil 3D 2026 em 2026-09-22 (build R25.1.179) passou o host Autodesk para .NET 10 (`acdbmgd.runtimeconfig.json` declara `"tfm": "net10.0"`), e todo projeto que referencia Autodesk parou com CS1705. Confirmado independente do codigo novo: sem os testes de bloco, a solucao ainda dava 12 erros. Correcao registrada em `docs/decisions/ADR-0009-net10-autodesk-host.md`:

- `Bridge`, `Tools.AutoCAD`, `Tools.Civil` e seus dois projetos de teste foram para `net10.0-windows`; o resto continua em .NET 8
- `scripts/package.ps1` tinha `net8.0-windows` fixo no caminho e empacotaria pasta vazia sem erro; agora le o TFM do `.csproj` e falha alto
- `PackageContents.xml` dizia `SeriesMin="R25.0"` (Civil 3D 2025), falso para um bridge .NET 10; agora `R25.1`
- o bundle exige o 2026 **atualizado**; o manifesto nao consegue expressar nivel de update dentro da serie

Observacoes registradas no relatorio, sem conclusao: componentes de rotulo do Civil 3D (`AeccTickLine`, `AeccTickCircle`) sobrevivem a exportacao como blocos; o handle `D7D90` e o mesmo bloco `_MP Marco Estacao` nos dois desenhos, com valor `MPE 92` num e `MPE 01` no outro - se handles sobrevivem a exportacao, seriam ancora de rastreio mais forte que nome de layer, e vale verificar de proposito.

## Dois Momentos do Projeto: Geometrico e Exportado

Registrado em 2026-09-19 a partir de par real de arquivos fornecido pelo operador. Detalhe completo e evidencia em `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`. **Conhecimento apenas; nada implementado.**

Um projeto rodoviario e trabalhado em dois momentos, e eles nao sao o mesmo desenho com menos recursos. Sao dois modelos de dado que respondem as mesmas perguntas por meios diferentes.

- **Geometrico**: alinhamento, superficie, greide e corredor sao objetos Civil 3D vivos. Estaca vem do objeto `Alignment`, exata.
- **Exportado**: a inteligencia Civil 3D sumiu. O eixo e polilinha, a estaca e texto, a superficie e hachura. A mesma pergunta tem que ser respondida a partir de primitivas.

Medido no par (mesmo projeto, dois momentos):

| | Geometrico | Exportado |
|---|---|---|
| Formato DWG | AC1032 | AC1015 |
| Definicoes de property set | 6 | 0 |
| Objetos com set anexado | 0 de 4146 | 0 |
| Estaqueamento | objeto `Alignment` | texto (`172+700`, `173+235`, ...) |

Dois achados, e o segundo e o maior:

1. **A exportacao destroi property set.** Nem as definicoes sobrevivem.
2. **Property set nao carrega dado neste projeto nem antes da exportacao.** Os 4146 objetos do geometrico devolveram `count: 0`. As 6 definicoes sao casca vazia: 3 do projeto, nunca preenchidas, e 3 herdadas do template de corredor da Autodesk.

**O que de fato atravessa os dois momentos e a convencao de layer**, unica estrutura presente nos dois arquivos:

```text
F-HZ-EIXO      F-VT-GREIDE            F-SC-SECAO
F-HZ-ESTACA    F-VT-TERRENO           F-SC-TABELA_ESTACA
F-HZ-SECAO     F-VT-COTAS-PROJETO     F-HATCH-PISTA-EXISTENTE
```

`HZ` planta, `VT` perfil longitudinal, `SC` secao transversal. **No momento geometrico voce pergunta ao objeto; no exportado voce pergunta ao layer.**

Consequencias para quem for implementar:

- **A convencao de layer e configuracao declarada, nunca codigo.** Mesma regra ja valida para nome de property set: `Dados_Rodovia` tem zero ocorrencias em `src/`, e `F-HZ-EIXO` tambem nao pode aparecer la. Uma tool do momento exportado recebe um mapa papel-para-layer como entrada.
- **O momento e explicito, nao autodetectado.** Tool que escolhe estrategia em silencio esconde qual usou. Prefira uma tool por momento, mais um detector barato que diz em que momento o desenho esta.
- **A resposta declara a origem do numero.** Estaca calculada sobre `Alignment` e exata; estaca lida de um MText vale o que valia quem plotou. Se as duas voltarem no mesmo formato, o agente trata as duas como fato.

Isto corrige uma analise anterior deste projeto que classificou `roadway_get_station` como redundante com `alignment_station_offset`. Duas tools que respondem a mesma pergunta a partir de **fontes de dado diferentes nao sao duplicatas**. O criterio certo e o mecanismo, nao o nome.

**Correcao de nome pendente**, nao feita ainda: `roadway_propertyset_write` nao tem uma linha de logica rodoviaria. Deveria ser `propertyset_set_batch` no modulo Civil, e o planejado `roadway_propertyset_read` deveria ser `propertyset_get_batch`. Corrigir enquanto so existe uma tool assim.

## Phase 3F - Teste de Sobrevivencia de Portadores de Dado (PLANEJADA)

Aberta em 2026-09-19 a pedido do operador (R2). **Nao implementada.** Detalhe da analise em `docs/DATA_CARRIERS.md`.

**Pergunta que a fase responde:** quais portadores de dado sobrevivem a exportacao do operador?

Hoje so ha uma resposta medida: **AEC Property Set nao sobrevive**. Nem os valores, nem as definicoes. Object Data e XData estao sem medicao, e nao se deve afirmar nada sobre eles ate rodar isto.

**Por que e uma fase e nao um ajuste:** o resultado decide o desenho do momento exportado inteiro. Se nenhum portador sobrevive, o exportado so tem layer e texto, e as tools desse momento leem geometria. Se algum sobrevive, ele vira o canal de dado da entrega e muda o que se constroi.

Procedimento, **uma unica rodada de exportacao para os tres**:

1. Copiar o geometrico de referencia para `artifacts\live-tests\`. Nunca o original.
2. Na **mesma hachura**, anexar os **quatro**:
   - Property Set com uma propriedade `Real` de valor conhecido
   - registro de Object Data com um campo real de valor conhecido
   - XData sob nome de aplicacao conhecido
   - XRecord em Extension Dictionary sob nome conhecido
3. Rodar a exportacao **do operador**, nao uma inventada pelo agente. Perguntar qual comando/ferramenta e, e observar uma execucao.
4. Ler os tres de volta no arquivo exportado.

Resultado esperado: uma linha por portador, sobreviveu ou nao.

**Lacuna de plataforma que a fase expoe:** hoje a plataforma escreve Property Set (7 tools validadas) e **nao escreve nem le Object Data nem XData**. Entao o passo 2 precisa ou de um probe descartavel, ou de adaptadores minimos. Probe descartavel e o caminho barato: a fase existe para medir, nao para entregar tools.

**XRecord entrou na fase em 2026-09-20**, depois da pesquisa que o operador trouxe. A Autodesk recomenda XRecord em vez de XData assim que a aplicacao guarda algo substancial: XData tem teto de 16 KB por entidade **compartilhado entre todas as aplicacoes**, com recomendacao de nao passar de ~2 KB e nunca de 4 KB por aplicacao. Se este MCP um dia precisar gravar estado proprio no objeto - o que gerou, qual versao, se precisa reprocessar, qual registro externo corresponde - o carregador documentado e XRecord. Medir os quatro de uma vez custa o mesmo que medir tres.

**Ponto de verificacao anexo (R3):** o operador informou que Object Data exporta para KMZ e que KMZ e entrega necessaria nos projetos. A busca no install do Civil 3D 2026 **nao achou** componente KML/KMZ, e `ManagedMapApi.dll` tem zero ocorrencia da string `kml`. Entao o mecanismo esta por confirmar. Perguntar ao operador qual comando produz o KMZ hoje e observar uma execucao e mais rapido e mais confiavel que inferir do install. Ate la, nao afirmar que Object Data chega ao KMZ.

## Proxima Fase Recomendada

**Revisado em 2026-09-24. A ordem oficial agora vive em `docs/ROADMAP_PROGRESS.md`, secao "Immediate Next Steps".** Esta secao so resume; se divergirem, vale o roadmap. Duas listas de proximos passos em dois documentos foi exatamente o que deixou este handoff desatualizado antes.

**Atualizacao 2026-09-30:** ha um plano registrado para ferramentas de correcao controlada de Surface/TIN, motivado pela analise ao vivo da superficie `TN RTK`. Isso e backlog/plano, nao autorizacao para implementar agora. Nao iniciar essa trilha sem o operador retomar explicitamente.

Plano registrado:

- Design: `docs/superpowers/specs/2026-09-30-surface-tin-correction-tools-design.md`
- Execucao futura: `docs/superpowers/plans/2026-09-30-surface-tin-correction-tools.md`
- Regra central: nunca editar `TN RTK` diretamente; criar e operar sobre copia, por exemplo `TN RTK_CORRIGIDA`.
- Developer Mode nao e caminho para essa tarefa; continua desabilitado e sem Roslyn/sandbox implementados.
- Tools planejadas: `surface_audit_tin`, `surface_copy`, `surface_rebuild`, `surface_set_build_options`, `surface_boundary_preview`, `surface_add_outer_boundary`, `surface_breakline_candidates`, `surface_add_standard_breakline`, `surface_tin_edit_preview`, `surface_swap_edge`, `surface_delete_tin_line`.

Ordem, por valor sobre esforco:

1. ~~Leituras de bloco~~ **feito em 2026-09-24, Phase 2J**, validado ao vivo nos dois momentos.
2. **Leitura do mapa papel-para-layer** (`ObjectLayerSettings`). A ancora de rastreio que o operador pediu padronizada.
3. **Detector de momento (D1).** Barato; tira a ambiguidade de 15 tools de uma vez.
4. **D4 renomear, depois D3 extrair** o pipeline de escrita confirmada, antes da terceira tool com confirmacao.
5. **Phase 3F** - uma rodada de exportacao, quatro portadores. Paralelizavel. Ver secao `## Phase 3F`.
6. **ADRs** das tres decisoes de 2026-09-19/20 (secao 35 exige).
7. **D2** nomenclatura de resposta. Maior, menos urgente, piora a cada resposta nova.

**Pausado por decisao do operador (2026-09-19):** `roadway_create_km_marker`. Nao implementar, nao propor, nao incluir em estimativa ate ele retomar. Se precisar mencionar, explicar a funcao primeiro em vez de pedir a decisao de bloco/polilinha/ponto COGO.

**Parado:** Survey COM alem de listagem, ate existir projeto Survey numa maquina de validacao.

Decisoes do operador com respostas: `docs/CONSOLIDATION_REVIEW.md`, secao "Operator decisions".

## Metodo Para Confirmar API .NET do Civil 3D

Nao invente membros de API. Em PowerShell 5.1 desta maquina a carga reflection-only falha para assemblies .NET 8, entao use leitura de metadados com um projeto .NET 8 descartavel:

```csharp
using var fs = File.OpenRead(@"C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll");
using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
var md = pe.GetMetadataReader();
// enumerar md.TypeDefinitions e imprimir propriedades/metodos publicos
```

`System.Reflection.Metadata` esta no proprio .NET 8, nao resolve dependencias e nao executa codigo do assembly. Foi assim que a Phase 2D confirmou `Alignment.GetProfileIds()`, `Profile.ElevationAt`, `Profile.GradeAt` e os membros herdados de `Autodesk.Civil.DatabaseServices.Entity`.

O build Release do Bridge tambem funciona como verificacao final, porque o compilador rejeita qualquer membro inexistente.

## Plano Sugerido Para A Proxima Fase

Use o mesmo fluxo que funcionou nas Phases 4E-C e 2D:

1. Confirmar a superficie da API por metadados antes de projetar o contrato.
2. Escrever testes RED no projeto de teste do modulo e observar a falha.
3. Adicionar contratos em `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`.
4. Estender o catalogo do modulo com descriptor READ/Low.
5. Estender a interface de adapter do modulo e o servico puro, mantendo regra de negocio fora do codigo Autodesk.
6. Implementar o adapter Autodesk por ultimo.
7. Atualizar `FakeBridgeHost.cs` com dispatch, manifest e schema, e elevar a contagem esperada de catalogo em `ServerFakeBridgeTests.cs`.
8. Rodar validacoes:

```powershell
dotnet test .\Civil3D.HybridMcp.sln -c Debug -v:q --nologo
dotnet build .\src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj -c Release -v:q -clp:ErrorsOnly --nologo
```

9. Instalar bundle e rodar o diagnostico offline.
10. Live smoke **sempre sobre copia** do DWG de teste, nunca sobre o original, encerrando a instancia de teste ao final.
11. Atualizar docs e `CHANGELOG.md`, criando o `docs/PHASE_*_LIVE_TEST.md` correspondente.

Regra de ambiguidade ja adotada: quando um localizador puder casar com mais de um objeto, retorne erro estruturado listando os candidatos em vez de escolher sozinho. Foi o que `profile_get` fez com `ProfileAmbiguous`.

## Tentativa de Live Smoke da Phase 2G em 2026-09-14

O que foi provado ao vivo, antes do bloqueio:

- `tools/list` respondeu com **39 tools**, incluindo `object_get` e `object_properties`
- `list_dialogs` respondeu com o host travado e identificou o bloqueio: `blocked_by_modal: true`, `blocking_dialog_title: "References - Not Found Files"`, janela principal desabilitada, a palete `Properties` desabilitada corretamente **nao** classificada como modal

O que bloqueou:

- `document_list` e qualquer tool despachada pelo host deram timeout enquanto o modal estava aberto
- o descritor de endpoint foi podado do registro durante o bloqueio: o heartbeat roda no idle loop, o idle loop estava travado pelo modal, o descritor envelheceu e a proxima leitura do registro o removeu
- consequencia pratica: **com o host bloqueado, a descoberta por registro deixa de funcionar, mas o pipe continua atendendo**. O cliente de live test foi reescrito para falar direto com o pipe por isso

Isso e a terceira vez que o mesmo modal bloqueia validacao ao vivo neste projeto (Phases 2E, 3C e agora 2G). Reforca a prioridade de `dismiss_dialog` com allowlist e titulo esperado obrigatorio.

Detalhe operacional: o `acad.exe` de teste foi encerrado ao final e nenhuma instancia ficou rodando. As copias de teste estao em `artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_object_read_20260914-150605.dwg` e `artifacts\live-tests\Way-153_25-179_object_read_20260914-150605.dwg`. O cliente em `artifacts\live-client` ja aceita `<pipeName> <instanceId>` e roda o roteiro completo da Phase 2G.

## Conhecimento Registrado: IBGE-PPP (Survey)

Em 2026-09-14 o usuario trouxe a base de conhecimento do projeto `PPP_IBGE` para dentro deste projeto. Ver `docs/SURVEY_IBGE_PPP.md`.

**Nada foi implementado, e isso foi pedido explicitamente.** O usuario vai desenhar o procedimento depois. O registro existe para que esse desenho parta de fato, nao de suposicao.

Objetivo final: trabalhar com dados de coletora RTK e importar com **base corrigida no IBGE**, em vez de base RTK bruta.

Pipeline ja validado fora desta plataforma:

```text
JPS (JAVAD) -> RINEX 2.11 (RTKLIB convbin) -> ZIP -> API IBGE-PPP
```

Dois fatos que dominam o resto:

1. **Altura da antena**: enviar a altura **vertical** ate o ARP, nunca a inclinada. No arquivo JAVAD validado, `1.5626 m` e a correta e `1.6200 m` e a inclinada. As duas estao no arquivo bruto, as duas parecem plausiveis, e nada a jusante acusa o erro.
2. **`.HCN` nao tem suporte**: e formato bruto CHC/CHCNAV; nem RTKLIB/convbin nem teqc produziram RINEX valido. Decisao do usuario: converter com software do fabricante (CHCNAV/iGx/CGO). Entao `.HCN` chega aqui **ja convertido**, nunca como bruto.

Antes de construir qualquer coisa nessa direcao, note o que ela implica:

- enviar ao IBGE seria a **primeira chamada de rede de saida** desta plataforma; tudo hoje e local, named pipe restrito ao usuario, nada sai da maquina
- envio **nao tem rollback**; uma vez enviado, saiu do controle do operador, o que coloca isso na mesma familia de uma operacao DESTRUCTIVE mesmo sem mutar desenho
- o precedente certo para a primeira metade ja existe: `survey_fbk_dry_run` analisa, reporta e nao toca em nada
- `convbin.exe` e uma dependencia de binario externo, forma nova neste projeto

As perguntas em aberto estao listadas no proprio documento e sao decisao do usuario, nao default de quem implementa.

## Onde Nao Avancar Ainda

Nao iniciar Developer Mode/Roslyn antes de fechar uma decisao clara de sandbox e risk analyzer.

Nao implementar FBK import ainda. O parser dry-run existe, mas importacao envolve Survey Database, possiveis commands/COM fallbacks e fluxo de rollback/progresso.

Nao implementar write/destructive Survey enquanto nao houver:

- preview claro;
- confirmacao quando aplicavel;
- escopo de objetos afetados;
- estrategia de rollback ou declaracao explicita de limitacao;
- DWG de teste confirmado;
- cobertura live real dos reads Survey ja implementados.

Nao usar UI Automation quando houver API .NET/COM programatica adequada.

## Checklist Antes de Dizer Que Concluiu Uma Proxima Fase

- [ ] Tests RED foram observados antes da implementacao principal.
- [ ] Superficie da API Autodesk confirmada por reflexao/SDK, sem inventar membros.
- [ ] Unit tests do modulo passaram.
- [ ] Integration tests passaram.
- [ ] Full solution test passou.
- [ ] Bridge Release build passou.
- [ ] Bundle diagnostic passou.
- [ ] Live smoke executado se a fase tocou bridge/Autodesk.
- [ ] Lacunas de cobertura live foram documentadas explicitamente, sem afirmar validacao que nao ocorreu.
- [ ] Documentacao atualizada.
- [ ] CHANGELOG atualizado.
- [ ] Nenhum `acad.exe` de teste ficou rodando.
- [ ] Operacoes destrutivas nao foram executadas.
