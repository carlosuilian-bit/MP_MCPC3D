# AGENTS.md — Civil3D Hybrid MCP

## 1. Propósito deste arquivo

Este arquivo contém as instruções permanentes para qualquer agente de desenvolvimento (especialmente Codex) que trabalhe neste diretório.

O objetivo do projeto é desenvolver uma plataforma MCP híbrida para Autodesk Civil 3D, combinando as melhores ideias de três projetos de referência:

- `hjlrosales/Civil3D_MCP`
- `barbosaihan/civil3d-mcp`
- `KevinGriffin-new/civil3d-mcp`

Os três repositórios devem ser tratados inicialmente como **referências técnicas independentes**. Não presumir que possam ser mesclados diretamente nem que possuam licenças compatíveis entre si.

O produto final deverá ser uma nova plataforma própria, modular, segura para desenhos de produção e extensível por IA.

---

# 2. Visão do produto

Construir um sistema em que um agente de IA consiga:

1. consultar o desenho atual do Civil 3D;
2. executar operações tipadas e previsíveis;
3. trabalhar com objetos nativos do Civil 3D;
4. operar sobre dados de rodovias, topografia e infraestrutura;
5. executar rotinas novas quando ainda não existir uma ferramenta pronta;
6. transformar rotinas novas e recorrentes em ferramentas permanentes;
7. trabalhar de forma segura em arquivos DWG reais;
8. manter rastreabilidade completa de tudo que foi alterado.

A plataforma deverá combinar três filosofias:

```text
                   AGENTE IA / CODEX
                          │
                          │ MCP
                          ▼
        ┌─────────────────────────────────┐
        │      CIVIL3D HYBRID MCP         │
        │                                 │
        │  1. Production Tools            │
        │     ferramentas tipadas         │
        │                                 │
        │  2. Survey Module               │
        │     topografia / survey         │
        │                                 │
        │  3. Developer Mode              │
        │     C# dinâmico controlado      │
        │                                 │
        │  4. Safety / Transactions       │
        │     validação / rollback        │
        └────────────────┬────────────────┘
                         │
                  IPC local seguro
                         │
                         ▼
                  Civil 3D Bridge
                         │
                         ▼
                  CIVIL 3D 2026
```

---

# 3. Princípios herdados dos projetos de referência

## 3.1. De `hjlrosales/Civil3D_MCP`

Usar como principal referência para:

- arquitetura orientada a produção;
- ferramentas MCP tipadas;
- schemas de entrada e saída;
- descoberta de ferramentas;
- bridge carregado pelo Civil 3D;
- empacotamento Autodesk `.bundle`;
- auto-load;
- Named Pipes ou outro IPC local restrito;
- gerenciamento de múltiplas instâncias;
- Document Lock;
- Transactions;
- commit/rollback;
- validação;
- tratamento estruturado de erro;
- confirmação de ações destrutivas;
- cancelamento;
- timeouts;
- logs;
- testes de robustez;
- separação entre servidor MCP e bridge Autodesk.

**Não copiar código automaticamente.**
Primeiro analisar licença, arquitetura e compatibilidade.

---

## 3.2. De `barbosaihan/civil3d-mcp`

Usar como principal referência para:

- execução dinâmica de C#;
- compilação em runtime com Roslyn;
- disponibilização controlada de:
  - `Document`
  - `CivilDocument`
  - `Database`
  - `Editor`
  - `Transaction`
- skills voltadas à Civil 3D .NET API;
- geração de operações inéditas pelo agente;
- transformação de conhecimento da API em código executável;
- sandbox;
- bloqueio de namespaces e recursos perigosos;
- ferramenta equivalente a `civil3d_query`;
- ferramenta equivalente a `civil3d_execute`;
- biblioteca de skills.

O modo dinâmico será tratado como **Developer Mode** e não como caminho normal de produção.

---

## 3.3. De `KevinGriffin-new/civil3d-mcp`

Usar como principal referência para:

### Topografia / Survey

- COGO Points;
- bearing;
- azimuth;
- Point Styles;
- Label Styles;
- Survey Working Folder;
- Survey Projects;
- Survey Networks;
- Control Points;
- Non-Control Points;
- Setups;
- Observations;
- Figures;
- Figure Prefix Database;
- FBK;
- importação de levantamento;
- criação de breaklines a partir de dados topográficos.

### Robustez de automação

Estudar e reaproveitar conceitualmente:

- detecção de janelas modais;
- identificação de Civil 3D bloqueado por diálogo;
- `list_dialogs`;
- `dismiss_dialog`;
- `cancel_command`;
- recuperação após modal inesperado.

A detecção de diálogos deverá fazer parte do **core da plataforma**, não apenas do módulo de topografia.

---

# 4. Regra fundamental: NÃO fundir os três projetos diretamente

O agente NÃO deve:

- copiar todos os arquivos dos três repositórios para um só;
- tentar resolver conflitos de namespaces cegamente;
- substituir um projeto pelo outro;
- modificar os repositórios de referência antes de entender suas arquiteturas;
- assumir compatibilidade de licenças;
- assumir compatibilidade de versões .NET;
- assumir compatibilidade de versões da API Autodesk.

Os três repositórios devem permanecer, inicialmente, **imutáveis**.

Tratá-los como:

```text
/references/
    /hjlrosales-Civil3D_MCP/
    /barbosaihan-civil3d-mcp/
    /KevinGriffin-civil3d-mcp/
```

Se os nomes reais forem diferentes, detectar os diretórios existentes.

O novo projeto deverá ficar separado, por exemplo:

```text
/src/
/tests/
/docs/
/tools/
```

ou em uma pasta própria:

```text
/Civil3D-Hybrid-MCP/
```

Não reorganizar os repositórios de referência sem necessidade.

---

# 5. Primeira tarefa obrigatória do Codex

ANTES DE PROGRAMAR QUALQUER FUNCIONALIDADE:

## Etapa A — Inventário

Inspecionar os três repositórios e produzir:

`docs/REFERENCE_ARCHITECTURE_AUDIT.md`

Esse documento deve conter, para cada projeto:

- linguagem;
- estrutura de diretórios;
- versão alvo do Civil 3D;
- versão alvo do AutoCAD;
- versão .NET;
- bibliotecas Autodesk utilizadas;
- protocolo de comunicação;
- modo de carregamento no Civil 3D;
- ferramentas MCP disponíveis;
- comandos de leitura;
- comandos de escrita;
- mecanismo de transação;
- mecanismo de rollback;
- mecanismo de Document Lock;
- mecanismo de confirmação;
- logging;
- timeout;
- cancelamento;
- suporte a múltiplas instâncias;
- tratamento de modal dialogs;
- suporte a Survey;
- suporte a Roslyn;
- sandbox;
- testes;
- licença.

## Etapa B — Licenças

Criar:

`docs/LICENSE_COMPATIBILITY.md`

Para cada repositório:

- identificar licença;
- registrar obrigações;
- verificar possibilidade de reutilização de código;
- verificar necessidade de atribuição;
- separar:
  - código que pode ser reutilizado;
  - código que deve apenas servir de referência;
  - ideias arquiteturais que podem ser reimplementadas.

**Não copiar código antes desta análise.**

## Etapa C — Matriz de funcionalidades

Criar:

`docs/FEATURE_MATRIX.md`

Comparar:

- hjlrosales
- barbosaihan
- KevinGriffin
- plataforma híbrida desejada

Usar colunas:

```text
Feature
Reference implementation
Reuse?
Reimplement?
Priority
Target module
Status
Notes
```

---

# 6. Arquitetura alvo

A arquitetura preferencial deverá ser:

```text
MCP Client
   │
   ▼
Hybrid MCP Server
   │
   ├── Tool Registry
   ├── Tool Schemas
   ├── Permissions
   ├── Confirmation Policy
   ├── Session Manager
   ├── Logging
   └── Developer Mode
   │
   ▼
Secure Local IPC
   │
   ▼
Civil 3D Bridge
   │
   ├── Core
   ├── AutoCAD API
   ├── Civil 3D API
   ├── Survey
   ├── Dialog Monitor
   └── Dynamic Execution Host
   │
   ▼
Autodesk Civil 3D
```

Preferir IPC local que não exponha desnecessariamente uma porta de rede.

Named Pipe com restrição ao usuário atual é a opção preferencial inicial.

Caso outro mecanismo seja adotado, documentar a justificativa.

---

# 7. Organização modular desejada

A implementação deve separar claramente:

```text
Core
AutoCAD
Civil3D
Survey
Roadway
DeveloperMode
Diagnostics
```

Exemplo conceitual:

```text
src/
  HybridMcp.Server/
  Civil3D.Bridge/
  Civil3D.Core/
  Civil3D.Tools.AutoCAD/
  Civil3D.Tools.Civil/
  Civil3D.Tools.Survey/
  Civil3D.Tools.Roadway/
  Civil3D.DeveloperMode/
  Civil3D.Diagnostics/
```

Não é obrigatório usar exatamente esses nomes.

O importante é evitar um único arquivo gigantesco de dispatcher.

---

# 8. Política de ferramentas

Existirão três classes de operação.

## Classe 1 — READ

Apenas consulta.

Exemplos:

- listar alinhamentos;
- ler propriedades;
- consultar estação/offset;
- consultar elevação;
- listar COGO Points;
- listar superfícies;
- consultar Property Sets.

Pode executar sem confirmação.

---

## Classe 2 — WRITE

Cria ou altera objetos sem destruição em massa.

Exemplos:

- criar COGO Point;
- renomear objeto;
- criar linha;
- criar alinhamento;
- alterar propriedade;
- adicionar breakline.

Deve usar:

- validação;
- Document Lock;
- Transaction;
- rollback automático em erro.

---

## Classe 3 — DESTRUCTIVE

Inclui:

- erase;
- delete;
- purge;
- substituir muitos blocos;
- remover alinhamentos;
- remover superfícies;
- remover corridors;
- apagar redes;
- alterar grande quantidade de objetos;
- sobrescrever propriedades em massa.

Exige confirmação explícita.

Antes da confirmação deve informar:

```text
ação
tipo de objeto
quantidade afetada
escopo
possibilidade de rollback
```

Nunca esconder uma operação destrutiva dentro de uma ferramenta aparentemente inofensiva.

---

# 9. Princípio de segurança

Por padrão:

```text
READ > WRITE > DESTRUCTIVE
```

O agente deve preferir a operação menos invasiva.

Antes de qualquer alteração em massa:

1. consultar objetos;
2. calcular escopo;
3. apresentar preview;
4. executar somente após autorização quando necessário.

---

# 10. Transactions

Toda operação de escrita deverá usar transações Autodesk adequadamente.

Fluxo mínimo:

```text
validate
  ↓
document lock
  ↓
start transaction
  ↓
execute
  ↓
validate result
  ↓
commit
```

Em erro:

```text
exception
  ↓
abort / rollback
  ↓
return structured error
```

Evitar operações parciais.

Se uma ferramenta alterar 500 objetos e falhar no objeto 499, preferir rollback completo salvo quando o comportamento parcial tiver sido explicitamente projetado e documentado.

---

# 11. Developer Mode — C# dinâmico

O projeto deverá conter um modo inspirado no `barbosaihan`.

Nome conceitual:

```text
civil3d_execute_csharp
```

ou equivalente.

## Regra principal

**Developer Mode fica DESABILITADO por padrão.**

Ele deve ser habilitado explicitamente.

---

## 11.1. Casos de uso

Utilizar quando:

- ainda não existe ferramenta pronta;
- operação é experimental;
- usuário está desenvolvendo uma automação;
- precisamos explorar uma parte da Civil 3D API;
- precisamos prototipar uma nova tool.

---

## 11.2. Fluxo

```text
pedido
  ↓
não existe tool apropriada
  ↓
gerar C#
  ↓
analisar código
  ↓
classificar risco
  ↓
mostrar preview quando houver escrita
  ↓
executar em Transaction
  ↓
retornar resultado
```

---

## 11.3. Sandbox

Bloquear por padrão:

- `System.Diagnostics.Process`
- shell execution;
- PowerShell;
- CMD;
- escrita arbitrária fora das pastas permitidas;
- delete arbitrário de arquivos;
- Registry;
- chamadas de rede;
- carregamento arbitrário de assemblies;
- reflection perigosa;
- P/Invoke;
- chamadas Win32 não autorizadas.

Permitir somente assemblies explicitamente aprovados.

---

## 11.4. Civil 3D destructive API

O sandbox de sistema operacional NÃO é suficiente.

Também analisar semanticamente o código procurando operações Autodesk potencialmente destrutivas, por exemplo:

- `.Erase(`
- exclusões em massa;
- remoção de definitions;
- purge;
- modificação de coleções;
- alterações globais.

Código dinâmico destrutivo exige confirmação explícita.

---

# 12. Promoção de código dinâmico para ferramenta permanente

Uma ideia central do projeto:

```text
necessidade nova
      ↓
Developer Mode
      ↓
C# gerado
      ↓
teste
      ↓
funcionou?
      ↓
rotina recorrente?
      ↓
transformar em Tool permanente
```

Quando uma rotina dinâmica for utilizada repetidamente:

1. mover lógica para módulo tipado;
2. criar schema;
3. criar testes;
4. documentar;
5. remover dependência do código gerado;
6. registrar em changelog.

O sistema deve evoluir gradualmente de improvisações para ferramentas confiáveis.

---

# 13. Módulo Survey / Topografia

Criar módulo próprio inspirado no KevinGriffin.

Prioridades:

## COGO

- list;
- get;
- create;
- update;
- delete com confirmação;
- Point Groups;
- Point Styles;
- Label Styles;
- Raw Description;
- Full Description;
- Easting;
- Northing;
- Elevation.

## Geometria topográfica

- bearing;
- azimuth;
- distance;
- traverse;
- linha por bearing;
- linha por azimuth;
- conversões angulares.

## Survey Database

Investigar suporte real da API instalada para:

- Working Folder;
- Survey Project;
- Survey Network;
- Control Point;
- Non-Control Point;
- Setup;
- Observation;
- Figure;
- Figure Prefix Database.

## FBK

Objetivo:

```text
FBK
 ↓
parse / validate
 ↓
Survey Project
 ↓
Survey Network
 ↓
observations
 ↓
figures
 ↓
drawing
 ↓
breaklines
 ↓
surface
```

Implementar `dry-run` para importações sempre que possível.

---

# 14. Modal Dialog Monitor

Implementar no core.

Objetivo:

detectar se o Civil 3D está impedido de continuar por:

- modal dialog;
- command ativo;
- janela de aviso;
- caixa de confirmação;
- erro;
- processamento pendente.

Ferramentas desejadas:

```text
civil3d_status
list_dialogs
dismiss_dialog
cancel_active_command
```

Nunca fechar automaticamente uma caixa de diálogo potencialmente destrutiva sem identificar seu conteúdo e sua origem.

---

# 15. Ferramentas prioritárias do MVP

## Core

```text
civil3d_ping
civil3d_status
drawing_info
document_list
document_activate
```

## Layers

```text
layer_list
layer_get
layer_create
layer_update
layer_set_current
```

## Objects

```text
object_get
object_properties
object_delete
object_select
```

## Blocks

```text
block_list
block_reference_list
block_get_attributes
block_set_attribute
block_replace
```

## Property Sets

```text
propertyset_list
propertyset_get
propertyset_set
```

## COGO

```text
cogo_list
cogo_get
cogo_create
cogo_update
```

## Surfaces

```text
surface_list
surface_get
surface_elevation_at_xy
surface_create
surface_add_breakline
surface_rebuild
```

## Alignments

```text
alignment_list
alignment_get
alignment_station_offset
alignment_point_location
alignment_create_from_polyline
```

## Profiles

```text
profile_list
profile_get
profile_elevation_at_station
```

## Corridors

```text
corridor_list
corridor_get
corridor_rebuild
```

Não tentar implementar tudo no primeiro ciclo.

---

# 16. Módulo rodoviário futuro

A plataforma deverá permitir tools de alto nível específicas de engenharia rodoviária.

Exemplos futuros:

```text
roadway_get_station
roadway_create_km_marker
roadway_station_offset_batch
roadway_extract_alignment_data

roadway_visibility_check
roadway_access_geometry
roadway_interference_check

roadway_propertyset_read
roadway_propertyset_write

roadway_export_kml
roadway_generate_layouts
roadway_generate_viewports
```

Essas ferramentas devem usar as tools genéricas abaixo delas.

Evitar duplicação de lógica.

## 16.1 Revisão desta lista (2026-09-19)

A lista acima foi escrita **antes** de existirem as tools genéricas embaixo dela.
Depois de medir um par real geométrico/exportado do mesmo projeto, três correções
se impõem. Evidência completa em `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`.

**a) Duplicação se mede por mecanismo, não por nome.**

Um projeto é trabalhado em **dois momentos**: o *geométrico*, onde alinhamento,
greide e superfície são objetos Civil 3D vivos, e o *exportado*, onde o eixo virou
polilinha, a estaca virou texto e a superfície virou hachura. A mesma pergunta é
respondida a partir de fontes de dado incomparáveis.

Logo, duas tools que respondem a mesma pergunta em momentos diferentes **não são
duplicatas**. `roadway_get_station` não duplica `alignment_station_offset`: no
geométrico é redundante, no exportado é uma ferramenta nova e legítima. O que
falta ao nome é dizer em que momento ele opera.

**b) As duas tools de property set desta lista não são rodoviárias.**

`roadway_propertyset_write`, já implementado, não tem uma linha de lógica de
rodovia: recebe `set_name`, handles e pares nome/valor, e funciona igual com
qualquer property set. Está no módulo Roadway por acidente de onde foi construído.

| Nome atual ou planejado | Deveria ser |
|---|---|
| `roadway_propertyset_write` | `propertyset_set_batch`, módulo Civil |
| `roadway_propertyset_read` | `propertyset_get_batch`, módulo Civil |

Correção pendente, a fazer enquanto só existe uma tool assim.

**c) A convenção de layer é configuração declarada, nunca código.**

No momento exportado a semântica não está em property set — ela não sobrevive à
exportação, e no par medido nem antes dela carregava dado. Quem atravessa os dois
momentos é o **nome do layer** (`F-HZ-EIXO`, `F-VT-GREIDE`, `F-SC-SECAO`).

Vale aqui a mesma regra já válida para nome de property set: assim como
`Dados_Rodovia` tem zero ocorrências em `src/`, `F-HZ-EIXO` também não pode
aparecer lá. Essa convenção é deste cliente e desta entrega; outro projeto terá
outra. Uma tool do momento exportado recebe um **mapa papel→layer** como entrada.

E a resposta deve declarar a origem do número: estaca calculada sobre um
`Alignment` é exata, estaca lida de um MText vale o que valia quem plotou. Se as
duas voltarem no mesmo formato, o agente trata as duas como fato.

**d) A convenção de layer tem origem programática, e ela é a âncora de rastreio.**

O mapa papel→layer não precisa ser digitado. Ele é uma configuração de desenho do
Civil 3D, definida no template e legível pela API .NET:

```text
CivilDocument.Settings -> DrawingSettings -> ObjectLayerSettings
  -> GetObjectLayerSetting(SettingsObjectLayerType) -> SettingsObjectLayer
       .ObjectType  .LayerName  .Modifier  .ModifierValue
```

A mesma configuração que decide em que layer um objeto Civil é desenhado no
momento geométrico decide em que layer os restos explodidos dele caem no momento
exportado. É o fio que atravessa os dois, e é legível na origem. Detalhe e
correspondência medida em `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`.

**e) `roadway_create_km_marker` está PAUSADA.**

Decisão do operador em 2026-09-19: ele ainda não compreendeu a função da
ferramenta. **Não implementar, não propor, não incluir em estimativa** até que ele
retome o assunto. Se precisar mencionar, explique primeiro o que a ferramenta
faria — colocar marcos quilométricos ao longo do eixo automaticamente — em vez de
pedir a decisão de bloco/polilinha/ponto COGO.

---

# 17. Compatibilidade Civil 3D

Alvo principal:

**Autodesk Civil 3D 2026**

Entretanto:

- NÃO assumir versão do .NET;
- NÃO assumir assembly versions;
- NÃO assumir caminhos de instalação.

O Codex deve inspecionar:

- `.csproj`;
- DLL references;
- Civil 3D SDK/API presente;
- AutoCAD managed assemblies.

Sempre preferir detecção/configuração ao invés de caminhos hardcoded.

Se for simples oferecer compatibilidade 2025 sem comprometer o projeto, arquitetar para isso.

---

# 18. Bundle Autodesk

Objetivo final de instalação:

```text
Civil3D.HybridMcp.bundle
```

instalável em:

```text
%APPDATA%\Autodesk\ApplicationPlugins
```

O bridge deverá carregar automaticamente quando o Civil 3D iniciar.

Evitar depender de `NETLOAD` em uso normal.

`NETLOAD` pode ser utilizado durante desenvolvimento.

---

# 19. Multiple Civil 3D Instances

O sistema deve prever:

```text
Civil 3D A
Civil 3D B
Civil 3D C
```

O MCP deverá saber:

- quais estão abertas;
- qual DWG cada uma possui;
- PID;
- versão;
- documento ativo;
- bridge endpoint.

Quando houver ambiguidade, não executar silenciosamente no DWG errado.

Preferir:

```text
instance_id
document_id
```

nas operações sensíveis.

---

# 20. Identidade de objetos

Nunca depender apenas de `Handle` ou `ObjectId` sem entender sua duração.

Criar abstração de referência de objeto que possa incluir:

```text
document
database
handle
objectId
type
name
```

Documentar claramente quais identificadores continuam válidos:

- durante a transação;
- durante a sessão;
- após salvar;
- após reabrir o DWG.

---

# 21. Resultados estruturados

Toda tool deve retornar resposta estruturada.

Exemplo:

```json
{
  "success": true,
  "tool": "alignment_station_offset",
  "document": "Projeto.dwg",
  "data": {
    "alignment": "BR040_EIXO",
    "station": 630000.0,
    "offset": 12.35
  },
  "warnings": []
}
```

Erros:

```json
{
  "success": false,
  "error": {
    "code": "ALIGNMENT_NOT_FOUND",
    "message": "Alignment not found.",
    "details": {}
  }
}
```

Não usar mensagens humanas como único protocolo entre bridge e MCP.

---

# 22. Logging

Criar logs úteis para desenvolvimento e auditoria.

Registrar:

- timestamp;
- tool;
- documento;
- instance;
- parâmetros relevantes;
- quantidade de objetos afetados;
- duração;
- resultado;
- erro;
- rollback;
- confirmação quando aplicável.

Não registrar dados sensíveis desnecessários.

Developer Mode deve registrar também:

- hash do código executado;
- classificação de risco;
- resultado da análise de sandbox.

---

# 23. Testes

Nenhuma nova ferramenta de produção deve ser considerada concluída sem testes adequados.

Tipos:

## Unitários

- schemas;
- validação;
- cálculos;
- parsing;
- safety checks.

## Integração

- MCP ↔ Bridge;
- Bridge ↔ Civil 3D;
- transactions;
- rollback;
- timeout;
- cancelamento.

## Smoke tests

Exemplo:

```text
open Civil 3D
ping
list layers
list alignments
read surface
create temporary object
rollback/delete temporary object
```

## Testes destrutivos

Somente em DWG de teste.

Nunca usar desenho real como fixture destrutiva.

---

# 24. DWG de teste

Criar ou utilizar DWGs específicos de laboratório.

Não executar testes automatizados destrutivos em:

- desenhos de clientes;
- produção;
- projetos reais;
- arquivos sem backup.

Se não houver DWG de teste disponível, criar procedimento manual documentado.

---

# 25. Política de alteração dos repositórios de referência

Por padrão:

```text
READ ONLY
```

Se for necessário testar uma modificação em um dos repositórios:

1. explicar por que;
2. criar branch separada;
3. não misturar com o projeto principal;
4. registrar patch;
5. preservar o original.

Preferir reimplementar a ideia no projeto híbrido.

---

# 26. Política de dependências

Evitar dependências desnecessárias.

Antes de adicionar package:

- justificar;
- verificar manutenção;
- verificar licença;
- verificar compatibilidade com Autodesk;
- verificar impacto de deployment.

Preferir BCL/.NET e bibliotecas já necessárias ao Civil 3D quando adequado.

---

# 27. Coding Style

Prioridades:

1. clareza;
2. segurança;
3. testabilidade;
4. modularidade;
5. desempenho.

Evitar:

- métodos gigantes;
- dispatchers com centenas de `if/else`;
- strings mágicas;
- reflection sem necessidade;
- hardcoded paths;
- catches silenciosos;
- transações abertas por tempo excessivo;
- acesso à API Autodesk fora da thread/contexto adequado.

---

# 28. Não esconder falhas

Se uma operação Civil 3D não for suportada pela API disponível:

NÃO simular sucesso.

Retornar claramente:

```text
NOT_SUPPORTED
API_LIMITATION
VERSION_INCOMPATIBLE
REQUIRES_COM
REQUIRES_UI_AUTOMATION
```

Se for necessário recorrer a:

- COM;
- AutoLISP;
- command line;
- UI Automation;

documentar explicitamente que houve fallback.

---

# 29. Prioridade de API

Preferência:

```text
Civil 3D .NET API
        ↓
AutoCAD .NET API
        ↓
Civil 3D COM API
        ↓
AutoCAD COM
        ↓
AutoLISP / Commands
        ↓
UI Automation
```

Quanto mais baixo na lista, maior deve ser a justificativa.

Não usar UI Automation quando existir API programática adequada.

---

# 30. Performance

Evitar chamadas MCP repetitivas quando uma operação em lote puder ser feita dentro de uma única Transaction.

Exemplo ruim:

```text
500 pontos
= 500 chamadas MCP
```

Preferir:

```text
cogo_create_batch([...])
```

quando fizer sentido.

Ferramentas em lote devem possuir:

- limite;
- preview;
- progresso;
- cancelamento;
- rollback.

---

# 31. Long-running operations

Para:

- rebuild de corridor;
- rebuild de surface;
- importações grandes;
- processamentos em lote;

implementar:

- progress;
- timeout configurável;
- cancellation;
- heartbeat.

Nunca deixar o MCP aparentar travamento indefinido.

---

# 32. Documentação obrigatória

Manter:

```text
README.md
docs/ARCHITECTURE.md
docs/REFERENCE_ARCHITECTURE_AUDIT.md
docs/LICENSE_COMPATIBILITY.md
docs/FEATURE_MATRIX.md
docs/SECURITY_MODEL.md
docs/DEVELOPER_MODE.md
docs/SURVEY_MODULE.md
docs/TOOL_CATALOG.md
docs/INSTALLATION.md
docs/TROUBLESHOOTING.md
CHANGELOG.md
```

Atualizar documentação junto com alterações relevantes.

---

# 33. Regras para o Codex durante implementação

Ao receber uma tarefa:

1. leia este `AGENTS.md`;
2. identifique o módulo envolvido;
3. procure implementação equivalente nos três repositórios;
4. compare as abordagens;
5. verifique licença antes de reutilizar código;
6. escolha a arquitetura mais segura;
7. implemente no projeto híbrido;
8. crie/atualize testes;
9. atualize documentação;
10. explique resumidamente as decisões.

Nunca escolher uma implementação apenas porque é a mais curta.

---

# 34. Antes de escrever código novo

Pesquisar primeiro no projeto.

Ordem:

```text
1. nosso projeto híbrido
2. hjlrosales
3. barbosaihan
4. KevinGriffin
5. Autodesk API / SDK disponível localmente
```

Evitar reinventar algo que já está bem resolvido.

---

# 35. Quando houver abordagens diferentes

Registrar decisão em:

```text
docs/decisions/
```

Formato ADR:

```text
ADR-0001-named-pipe.md
ADR-0002-dynamic-csharp.md
ADR-0003-tool-permissions.md
```

Cada ADR deve conter:

```text
Context
Decision
Alternatives
Consequences
```

---

# 36. Meta principal de segurança

O sistema deve ser capaz de operar sobre um DWG real sem exigir que o usuário tema que uma interpretação errada do agente destrua o projeto.

Portanto:

```text
previsibilidade > conveniência
rollback > velocidade
preview > execução cega
tools tipadas > código dinâmico
```

Developer Mode existe para ampliar capacidade, não para substituir a arquitetura segura.

---

# 37. Meta principal de autonomia

Ao mesmo tempo, o sistema não deve ficar limitado às tools criadas previamente.

Portanto:

```text
tool inexistente
      ↓
Developer Mode
      ↓
prototipagem
      ↓
validação
      ↓
promoção para tool
```

Esse ciclo é uma característica central da plataforma.

---

# 38. Definição de sucesso do projeto

Considerar o núcleo bem-sucedido quando for possível:

1. abrir Civil 3D 2026;
2. bridge carregar automaticamente;
3. cliente MCP detectar a instância;
4. listar documentos;
5. listar layers;
6. listar alinhamentos;
7. listar superfícies;
8. consultar station/offset;
9. consultar elevation at XY;
10. listar COGO Points;
11. alterar um objeto com transaction segura;
12. desfazer/rollback corretamente em erro;
13. detectar modal dialog;
14. executar C# dinâmico controlado;
15. importar ou operar um fluxo Survey;
16. registrar tudo em log.

---

# 39. Roadmap sugerido

## Fase 0 — Auditoria

- inventário dos três projetos;
- licenças;
- matriz de funcionalidades;
- ADRs iniciais.

## Fase 1 — Core

- MCP server;
- bridge;
- secure IPC;
- session discovery;
- ping/status;
- bundle.

## Fase 2 — Read-only Civil 3D

- drawing;
- layers;
- objects;
- alignments;
- surfaces;
- COGO.

## Fase 3 — Safe Write

- transactions;
- locks;
- permissions;
- confirmation;
- rollback.

## Fase 4 — Survey

- bearing/azimuth;
- COGO;
- Survey Database;
- FBK;
- Figure Prefix.

## Fase 5 — Developer Mode

- Roslyn;
- sandbox;
- risk analyzer;
- dynamic C#.

## Fase 6 — Roadway

- tools de rodovia;
- Property Sets;
- KM;
- estação/offset;
- automações de projetos.

> **Compatibilização de 2026-09-24.** Dois itens desta lista mudaram de lugar.
> *Property Sets* saiu da Fase 6: as tools de property set são genéricas e
> pertencem ao módulo Civil (seção 16.1b). *KM* está pausado por decisão do
> operador (seção 16.1e). Entrou o momento exportado: estaca a partir do eixo
> exportado, e exportação KMZ via Object Data. O percentual e a ordem oficial de
> trabalho de todas as fases vivem em `docs/ROADMAP_PROGRESS.md`, que agora
> publica três números distintos — núcleo (seção 38), MVP (seções 14-15) e
> plataforma inteira — em vez de um só.

## Fase 7 — Production Hardening

- stress;
- multi-instance;
- cancellation;
- recovery;
- packaging;
- installer;
- diagnostics.

---

# 40. Primeira ação ao abrir este projeto

Se ainda não existirem os documentos de auditoria:

**NÃO COMEÇAR CODIFICANDO.**

Executar primeiro:

```text
1. localizar os três repositórios;
2. mapear estrutura;
3. ler READMEs;
4. ler manifests / package files / csproj;
5. identificar licenças;
6. localizar bridge;
7. localizar MCP server;
8. localizar dispatchers;
9. localizar tools;
10. localizar testes;
```

Depois criar:

```text
docs/REFERENCE_ARCHITECTURE_AUDIT.md
docs/LICENSE_COMPATIBILITY.md
docs/FEATURE_MATRIX.md
```

Somente então propor a arquitetura inicial.

---

# 41. Filosofia final

Não estamos criando apenas um "MCP para Civil 3D".

Estamos criando uma plataforma em que:

```text
IA
 │
 ├── usa ferramentas seguras quando elas existem
 │
 ├── entende Civil 3D
 │
 ├── entende topografia
 │
 ├── entende rodovias
 │
 ├── consegue desenvolver operações novas
 │
 └── transforma operações úteis em ferramentas permanentes
 │
 ▼
Civil 3D
```

A combinação desejada é:

```text
Robustez do hjlrosales
        +
Flexibilidade do barbosaihan
        +
Survey do KevinGriffin
        =
Civil3D Hybrid MCP
```

Esse princípio deve orientar todas as decisões arquiteturais do projeto.
