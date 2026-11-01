import { zodResolver } from '@hookform/resolvers/zod';
import {
  ArrowRightIcon,
  CircleAlertIcon,
  EyeIcon,
  EyeOffIcon,
  LockIcon,
  MailIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { useOpenFarmApiEndpointsAuthLoginEndpoint } from '@/lib/api/generated/auth/auth';
import { useAuth } from '@/lib/auth/useAuth';

const loginSchema = z.object({
  username: z.string().min(1, 'Username is required'),
  password: z.string().min(1, 'Password is required'),
});

type LoginFormValues = z.infer<typeof loginSchema>;

const inputClass =
  'h-[46px] rounded-[10px] bg-card pl-10 text-[15px] focus-visible:border-primary focus-visible:ring-primary/20 md:text-[15px] dark:bg-card';
const leadIconClass =
  'pointer-events-none absolute top-1/2 left-3.5 size-[17px] -translate-y-1/2 text-muted-foreground';

export function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [showPassword, setShowPassword] = useState(false);
  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: '', password: '' },
  });

  const loginMutation = useOpenFarmApiEndpointsAuthLoginEndpoint({
    mutation: {
      onSuccess: (response) => {
        const token = response.data.token;
        if (!token) {
          toast.error('Could not sign in. No token returned.');
          return;
        }
        auth.login(token);
        const target = new URLSearchParams(window.location.search).get(
          'returnTo',
        );
        navigate(
          target?.startsWith('/') && !target.startsWith('//') ? target : '/',
          { replace: true },
        );
      },
    },
  });
  const failed = loginMutation.isError;

  function onSubmit(values: LoginFormValues) {
    loginMutation.mutate({ data: values });
  }

  return (
    <div className="flex min-h-svh items-center justify-center bg-background px-6 py-10">
      <div className="w-full max-w-[368px]">
        <img
          src="/favicon.svg"
          alt="OpenFarm"
          className="mx-auto mb-6 size-[54px] drop-shadow-[0_4px_10px_rgb(21_96_245/0.35)]"
        />
        <div className="mb-6 text-center">
          <h1 className="text-[23px] font-semibold tracking-[-0.022em]">
            Welcome back
          </h1>
          <p className="mt-[7px] text-sm/normal text-muted-foreground">
            Sign in to your OpenFarm workspace to continue.
          </p>
        </div>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-[15px]"
          >
            <FormField
              control={form.control}
              name="username"
              render={({ field, fieldState }) => (
                <FormItem>
                  <FormLabel className="text-[13px]">Username</FormLabel>
                  <div className="relative">
                    <MailIcon className={leadIconClass} aria-hidden />
                    <FormControl>
                      <Input
                        autoComplete="username"
                        placeholder="Enter username"
                        className={inputClass}
                        aria-invalid={failed || !!fieldState.error}
                        {...field}
                      />
                    </FormControl>
                  </div>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="password"
              render={({ field, fieldState }) => (
                <FormItem>
                  <FormLabel className="text-[13px]">Password</FormLabel>
                  <div className="relative">
                    <LockIcon className={leadIconClass} aria-hidden />
                    <FormControl>
                      <Input
                        type={showPassword ? 'text' : 'password'}
                        autoComplete="current-password"
                        placeholder="••••••••"
                        className={`${inputClass} pr-11`}
                        aria-invalid={failed || !!fieldState.error}
                        {...field}
                      />
                    </FormControl>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="absolute top-1/2 right-1.5 size-[34px] -translate-y-1/2 text-muted-foreground"
                      aria-label={
                        showPassword ? 'Hide password' : 'Show password'
                      }
                      onClick={() => setShowPassword((shown) => !shown)}
                    >
                      {showPassword ? <EyeOffIcon /> : <EyeIcon />}
                    </Button>
                  </div>
                  <FormMessage />
                </FormItem>
              )}
            />
            {failed && (
              <p
                role="alert"
                className="-mt-1.5 flex items-center gap-1.5 text-[12.5px] text-destructive"
              >
                <CircleAlertIcon className="size-3.5" aria-hidden />
                Check username and password.
              </p>
            )}
            <Button
              type="submit"
              disabled={loginMutation.isPending}
              className="mt-1 h-[46px] w-full rounded-[10px] text-[15px] font-semibold"
            >
              {loginMutation.isPending ? (
                <>
                  <Spinner data-icon="inline-start" />
                  Signing in…
                </>
              ) : (
                <>
                  Sign in
                  <ArrowRightIcon data-icon="inline-end" />
                </>
              )}
            </Button>
          </form>
        </Form>
      </div>
    </div>
  );
}
