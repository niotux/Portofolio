namespace Humans;

public sealed partial class PromptStore
{
  // The v1 storage protocol stays compatible across HH code versions; an incompatible change needs new names.
  // Live prompts are what every person of a version runs at its next operation. Publications are numbered per version
  // and immutable.
  private const string Schema = """
    DO $$ BEGIN PERFORM pg_advisory_xact_lock(hashtextextended('hh-prompt-schema', 0)); END $$;
    CREATE TABLE IF NOT EXISTS hh_prompt_v1_live (
      version text PRIMARY KEY, content jsonb NOT NULL, revision bigint NOT NULL,
      changed_at timestamptz NOT NULL DEFAULT now()
    );
    CREATE TABLE IF NOT EXISTS hh_prompt_v1_publications (
      version text NOT NULL REFERENCES hh_prompt_v1_live(version), number int NOT NULL, label text NOT NULL,
      content jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(version, number)
    );
    CREATE OR REPLACE FUNCTION hh_prompt_v1_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'Published prompts cannot be edited or deleted';
    END $$;
    DO $$ BEGIN
      IF NOT EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid = 'hh_prompt_v1_publications'::regclass AND tgname = 'hh_prompt_v1_immutable') THEN
        CREATE TRIGGER hh_prompt_v1_immutable BEFORE UPDATE OR DELETE ON hh_prompt_v1_publications FOR EACH ROW EXECUTE FUNCTION hh_prompt_v1_immutable();
      END IF;
    END $$;
    """;
}
