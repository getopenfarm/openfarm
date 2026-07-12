import type { LucideIcon } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';

interface ComingSoonPageProps {
  title: string;
  description: string;
  phase?: number;
  icon?: LucideIcon;
}

export function ComingSoonPage({
  title,
  description,
  phase,
  icon: Icon,
}: ComingSoonPageProps) {
  return (
    <Card>
      <CardContent className="flex flex-col items-center gap-3 py-16 text-center">
        {Icon ? (
          <Icon className="size-8 text-muted-foreground" aria-hidden="true" />
        ) : null}
        <h2 className="font-heading text-lg font-semibold">{title}</h2>
        <p className="max-w-sm text-sm text-muted-foreground">{description}</p>
        {phase !== undefined ? (
          <span className="text-xs text-muted-foreground">
            Coming in Phase {phase}
          </span>
        ) : null}
      </CardContent>
    </Card>
  );
}
